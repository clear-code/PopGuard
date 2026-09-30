using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WindowFilter;

/// <summary>JSON ファイル 1 件分の形。</summary>
internal sealed class RuleFile
{
    public List<RuleEntry>? Rules { get; set; }
}

/// <summary>JSON のルール 1 件分の形（生の文字列）。</summary>
internal sealed class RuleEntry
{
    public bool Enabled { get; set; } = true;
    public string? Process { get; set; }
    public string? Title { get; set; }
    public string? Class { get; set; }
    public string? Hide { get; set; }

    // 最前面（TOPMOST）だけを対象にするか。
    // 【現状未実装】設定としては受け付けるが、内部では常に TOPMOST のみを対象にする。
    // 将来 false（TOPMOST 以外も対象）を実装する余地として残してある。
    public bool TopMostOnly { get; set; } = true;
}

/// <summary>
/// ワイルドカードをコンパイル済みにした、判定用のルール。
/// プロセス名は必須。タイトル／クラスは空なら「何でも一致」。
/// </summary>
internal sealed class TargetRule
{
    private readonly Regex _process;   // 必須なので非 null
    private readonly Regex? _title;    // null = 絞らない
    private readonly Regex? _class;    // null = 絞らない

    public HideMethod Hide { get; }

    /// <summary>
    /// TOPMOST のみを対象にするか。【現状 WindowFilter では未使用】。
    /// 設定を残すためにパースはするが、判定では使っていない（常に TOPMOST のみ対象）。
    /// </summary>
    public bool TopMostOnly { get; }

    private TargetRule(Regex process, Regex? title, Regex? className, HideMethod hide, bool topMostOnly)
    {
        _process = process;
        _title = title;
        _class = className;
        Hide = hide;
        TopMostOnly = topMostOnly;
    }

    /// <summary>安いプロセス名だけの一次判定（タイトル取得前のふるい落とし用）。</summary>
    public bool MatchesProcess(string process) => _process.IsMatch(process ?? string.Empty);

    /// <summary>プロセス・クラス・タイトルすべての一致。</summary>
    public bool Matches(string process, string className, string title)
        => _process.IsMatch(process ?? string.Empty)
           && (_class is null || _class.IsMatch(className ?? string.Empty))
           && (_title is null || _title.IsMatch(title ?? string.Empty));

    /// <summary>
    /// JSON エントリからルールを作る。プロセス名が空／ワイルドカードのみのものは
    /// 「何にでも一致してしまう」ため無効として null を返す。
    /// </summary>
    public static TargetRule? TryCreate(RuleEntry e, out string? invalidReason)
    {
        invalidReason = null;

        Regex? process = Wildcard.Compile(e.Process);
        if (process is null)
        {
            invalidReason = "process が空、または * / ? だけです";
            return null;
        }

        Regex? title = Wildcard.Compile(e.Title);
        Regex? className = Wildcard.Compile(e.Class);
        HideMethod hide = ParseHide(e.Hide);

        return new TargetRule(process, title, className, hide, e.TopMostOnly);
    }

    private static HideMethod ParseHide(string? s)
    {
        if (string.Equals(s, "Minimize", StringComparison.OrdinalIgnoreCase))
        {
            return HideMethod.Minimize;
        }
        if (string.Equals(s, "Hide", StringComparison.OrdinalIgnoreCase))
        {
            return HideMethod.Hide;
        }
        return HideMethod.Bottom; // 既定
    }
}

/// <summary>ワイルドカード（* と ?）を全体一致・大文字小文字無視の正規表現へ変換する。</summary>
internal static class Wildcard
{
    /// <summary>空／null は「絞らない」を意味する null を返す。* / ? だけのパターンも null。</summary>
    public static Regex? Compile(string? pattern)
    {
        string p = (pattern ?? string.Empty).Trim();
        if (p.Length == 0)
        {
            return null;
        }

        bool onlyWildcards = true;
        foreach (char c in p)
        {
            if (c != '*' && c != '?')
            {
                onlyWildcards = false;
                break;
            }
        }
        if (onlyWildcards)
        {
            return null;
        }

        var sb = new StringBuilder("^");
        foreach (char c in p)
        {
            sb.Append(c switch
            {
                '*' => ".*",
                '?' => ".",
                _ => Regex.Escape(c.ToString()),
            });
        }
        sb.Append('$');

        try
        {
            return new Regex(sb.ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}

/// <summary>ルール設定ファイル（JSON）の読み込み。無ければサンプルを書き出す。</summary>
internal static class RulesStore
{
    public static string FilePath => Path.Combine(AppContext.BaseDirectory, "WindowFilter.rules.json");

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static List<TargetRule> Load()
    {
        if (!File.Exists(FilePath))
        {
            TryWriteSample();
            Log.Line("rules: ファイルが無いためサンプルを生成しました。何も抑止しません。 path=" + FilePath);
            return new List<TargetRule>();
        }

        RuleFile? file;
        try
        {
            file = JsonSerializer.Deserialize<RuleFile>(File.ReadAllText(FilePath), ReadOptions);
        }
        catch (Exception ex)
        {
            Log.Line("rules: JSON の読み込みに失敗しました（何も抑止しません）: " + ex.Message);
            return new List<TargetRule>();
        }

        var rules = new List<TargetRule>();
        int invalid = 0;
        int disabled = 0;

        foreach (RuleEntry e in file?.Rules ?? new List<RuleEntry>())
        {
            if (!e.Enabled)
            {
                disabled++;
                continue;
            }

            TargetRule? rule = TargetRule.TryCreate(e, out string? reason);
            if (rule is null)
            {
                invalid++;
                Log.Line("rules: 無効なルールを飛ばしました（" + reason + "）");
                continue;
            }

            rules.Add(rule);
        }

        Log.Line($"rules: 有効={rules.Count} 無効={invalid} 無効化={disabled} path={FilePath}");
        return rules;
    }

    private static void TryWriteSample()
    {
        try
        {
            var sample = new RuleFile
            {
                Rules = new List<RuleEntry>
                {
                    new()
                    {
                        Enabled = false,
                        Process = "SomeNotifier",
                        Title = "*通知*",
                        Class = "",
                        Hide = "Bottom",
                        TopMostOnly = true,
                    },
                },
            };

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // 日本語をそのまま出す
            };

            File.WriteAllText(FilePath, JsonSerializer.Serialize(sample, options), new UTF8Encoding(false));
        }
        catch
        {
            // サンプルが書けなくても致命的ではない。
        }
    }
}
