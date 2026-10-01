using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WindowFilter;

/// <summary>JSON ファイル 1 件分の形。</summary>
internal sealed class RuleFile
{
    public List<RuleEntry>? Rules { get; set; }

    // トレイメニューに出す「抑止時間」の候補。省略時は既定の候補を使う。
    public List<DurationEntry>? Durations { get; set; }

    // Windows 11 のフォーカス セッション中に自動で抑止するか（省略時 true）。
    public bool? AutoSuppressDuringFocus { get; set; }
}

/// <summary>抑止時間の候補 1 件分（JSON）。minutes が 0 以下なら「無制限」。</summary>
internal sealed class DurationEntry
{
    public string? Label { get; set; }
    public int Minutes { get; set; }
}

/// <summary>抑止時間の候補（コンパイル済み）。<see cref="Duration"/> が null なら無制限。</summary>
internal sealed class DurationOption
{
    public string Label { get; }
    public TimeSpan? Duration { get; }

    public DurationOption(string label, TimeSpan? duration)
    {
        Label = label;
        Duration = duration;
    }
}

/// <summary>設定ファイル全体（ルール＋抑止時間の候補）。</summary>
internal sealed class AppConfig
{
    public IReadOnlyList<TargetRule> Rules { get; }
    public IReadOnlyList<DurationOption> Durations { get; }
    public bool AutoSuppressDuringFocus { get; }

    public AppConfig(
        IReadOnlyList<TargetRule> rules,
        IReadOnlyList<DurationOption> durations,
        bool autoSuppressDuringFocus)
    {
        Rules = rules;
        Durations = durations;
        AutoSuppressDuringFocus = autoSuppressDuringFocus;
    }
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

    public static AppConfig Load()
    {
        if (!File.Exists(FilePath))
        {
            TryWriteSample();
            Logger.Line("rules: ファイルが無いためサンプルを生成しました。何も抑止しません。 path=" + FilePath);
            return new AppConfig(new List<TargetRule>(), DefaultDurations(), true);
        }

        RuleFile? file;
        try
        {
            file = JsonSerializer.Deserialize<RuleFile>(File.ReadAllText(FilePath), ReadOptions);
        }
        catch (Exception ex)
        {
            Logger.Line("rules: JSON の読み込みに失敗しました（何も抑止しません）: " + ex.Message);
            return new AppConfig(new List<TargetRule>(), DefaultDurations(), true);
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
                Logger.Line("rules: 無効なルールを飛ばしました（" + reason + "）");
                continue;
            }

            rules.Add(rule);
        }

        IReadOnlyList<DurationOption> durations = BuildDurations(file?.Durations);
        bool autoFocus = file?.AutoSuppressDuringFocus ?? true;

        Logger.Line($"rules: 有効={rules.Count} 無効={invalid} 無効化={disabled} 時間候補={durations.Count} フォーカス連動={autoFocus} path={FilePath}");
        return new AppConfig(rules, durations, autoFocus);
    }

    /// <summary>設定の候補をコンパイルする。空／未指定なら既定の候補を使う。</summary>
    private static IReadOnlyList<DurationOption> BuildDurations(List<DurationEntry>? entries)
    {
        if (entries is null || entries.Count == 0)
        {
            return DefaultDurations();
        }

        var list = new List<DurationOption>();
        foreach (DurationEntry e in entries)
        {
            TimeSpan? duration = e.Minutes > 0 ? TimeSpan.FromMinutes(e.Minutes) : null; // 0 以下 = 無制限
            string label = string.IsNullOrWhiteSpace(e.Label) ? AutoLabel(e.Minutes) : e.Label.Trim();
            list.Add(new DurationOption(label, duration));
        }
        return list;
    }

    /// <summary>既定の候補：30分 / 1時間 / 2時間 / 一日 / 無制限。</summary>
    private static IReadOnlyList<DurationOption> DefaultDurations() => new List<DurationOption>
    {
        new("30分", TimeSpan.FromMinutes(30)),
        new("1時間", TimeSpan.FromHours(1)),
        new("2時間", TimeSpan.FromHours(2)),
        new("一日", TimeSpan.FromDays(1)),
        new("無制限", null),
    };

    private static string AutoLabel(int minutes)
    {
        if (minutes <= 0)
        {
            return "無制限";
        }
        if (minutes % 1440 == 0)
        {
            return $"{minutes / 1440}日";
        }
        if (minutes % 60 == 0)
        {
            return $"{minutes / 60}時間";
        }
        return $"{minutes}分";
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
                // トレイメニューに出す抑止時間の候補（minutes が 0 以下なら無制限）。
                // この durations 自体を省略すると、同じ既定候補が使われる。
                Durations = new List<DurationEntry>
                {
                    new() { Label = "30分", Minutes = 30 },
                    new() { Label = "1時間", Minutes = 60 },
                    new() { Label = "2時間", Minutes = 120 },
                    new() { Label = "一日", Minutes = 1440 },
                    new() { Label = "無制限", Minutes = 0 },
                },
                // Windows 11 のフォーカス セッション中は自動で抑止する（手動の応答不可は対象外）。
                AutoSuppressDuringFocus = true,
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
