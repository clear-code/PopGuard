using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WindowFilter;

/// <summary>Shape of one config JSON file.</summary>
internal sealed class RuleFile
{
    public List<RuleEntry>? Rules { get; set; }

    // Suppression-duration options shown in the tray menu. Defaults are used when omitted.
    public List<DurationEntry>? Durations { get; set; }

    // Whether to auto-suppress during a Windows 11 focus session (default true).
    public bool? AutoSuppressDuringFocus { get; set; }
}

/// <summary>One suppression-duration option (JSON). minutes &lt;= 0 means unlimited.</summary>
internal sealed class DurationEntry
{
    public string? Label { get; set; }
    public int Minutes { get; set; }
}

/// <summary>A compiled suppression-duration option. A null <see cref="Duration"/> means unlimited.</summary>
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

/// <summary>The whole config (rules + duration options).</summary>
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

/// <summary>Shape of one rule in JSON (raw strings).</summary>
internal sealed class RuleEntry
{
    public bool Enabled { get; set; } = true;
    public string? Process { get; set; }
    public string? Title { get; set; }
    public string? Class { get; set; }
    public string? Hide { get; set; }

    // Whether to target only topmost (TOPMOST) windows.
    // [Currently not implemented] accepted as config, but internally always TOPMOST-only.
    // Kept to leave room for implementing false (also target non-TOPMOST) later.
    public bool TopMostOnly { get; set; } = true;
}

/// <summary>
/// A rule with wildcards pre-compiled for matching.
/// Process name is required. An empty title/class means "match anything".
/// </summary>
internal sealed class TargetRule
{
    private readonly Regex _process;   // required, so non-null
    private readonly Regex? _title;    // null = no filter
    private readonly Regex? _class;    // null = no filter

    public HideMethod Hide { get; }

    /// <summary>
    /// Whether to target only TOPMOST windows. [Currently unused by WindowFilter.]
    /// Parsed so the config is preserved, but not used in matching (always TOPMOST-only).
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

    /// <summary>Cheap process-name-only pre-check (to sieve out candidates before the title lookup).</summary>
    public bool MatchesProcess(string process) => _process.IsMatch(process ?? string.Empty);

    /// <summary>Match on process, class, and title.</summary>
    public bool Matches(string process, string className, string title)
        => _process.IsMatch(process ?? string.Empty)
           && (_class is null || _class.IsMatch(className ?? string.Empty))
           && (_title is null || _title.IsMatch(title ?? string.Empty));

    /// <summary>
    /// Build a rule from a JSON entry. An empty or wildcard-only process name is rejected
    /// (it would match anything), returning null.
    /// </summary>
    public static TargetRule? TryCreate(RuleEntry e, out string? invalidReason)
    {
        invalidReason = null;

        Regex? process = Wildcard.Compile(e.Process);
        if (process is null)
        {
            invalidReason = "process is empty or only * / ?";
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
        return HideMethod.Bottom; // default
    }
}

/// <summary>Converts wildcards (* and ?) into a whole-match, case-insensitive regex.</summary>
internal static class Wildcard
{
    /// <summary>Empty/null returns null, meaning "no filter". A * / ? only pattern also returns null.</summary>
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

/// <summary>Loads the rule config file (JSON). Writes a sample if it does not exist.</summary>
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
            Logger.Line("rules: file not found, wrote a sample. Nothing will be suppressed. path=" + FilePath);
            return new AppConfig(new List<TargetRule>(), DefaultDurations(), true);
        }

        RuleFile? file;
        try
        {
            file = JsonSerializer.Deserialize<RuleFile>(File.ReadAllText(FilePath), ReadOptions);
        }
        catch (Exception ex)
        {
            Logger.Line("rules: failed to read JSON (nothing will be suppressed): " + ex.Message);
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
                Logger.Line("rules: skipped invalid rule (" + reason + ")");
                continue;
            }

            rules.Add(rule);
        }

        IReadOnlyList<DurationOption> durations = BuildDurations(file?.Durations);
        bool autoFocus = file?.AutoSuppressDuringFocus ?? true;

        Logger.Line($"rules: enabled={rules.Count} invalid={invalid} disabled={disabled} durations={durations.Count} focusSync={autoFocus} path={FilePath}");
        return new AppConfig(rules, durations, autoFocus);
    }

    /// <summary>Compile the duration options. Use the defaults when empty/omitted.</summary>
    private static IReadOnlyList<DurationOption> BuildDurations(List<DurationEntry>? entries)
    {
        if (entries is null || entries.Count == 0)
        {
            return DefaultDurations();
        }

        var list = new List<DurationOption>();
        foreach (DurationEntry e in entries)
        {
            TimeSpan? duration = e.Minutes > 0 ? TimeSpan.FromMinutes(e.Minutes) : null; // <= 0 = unlimited
            string label = string.IsNullOrWhiteSpace(e.Label) ? AutoLabel(e.Minutes) : e.Label.Trim();
            list.Add(new DurationOption(label, duration));
        }
        return list;
    }

    /// <summary>Default options: 30 min / 1 hour / 2 hours / 1 day / unlimited (labels are user-facing, JP).</summary>
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
                // Suppression-duration options for the tray menu (minutes <= 0 means unlimited).
                // Omitting durations entirely falls back to the same defaults.
                Durations = new List<DurationEntry>
                {
                    new() { Label = "30分", Minutes = 30 },
                    new() { Label = "1時間", Minutes = 60 },
                    new() { Label = "2時間", Minutes = 120 },
                    new() { Label = "一日", Minutes = 1440 },
                    new() { Label = "無制限", Minutes = 0 },
                },
                // Auto-suppress during a Windows 11 focus session (manual Do Not Disturb is not covered).
                AutoSuppressDuringFocus = true,
            };

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // emit Japanese as-is
            };

            File.WriteAllText(FilePath, JsonSerializer.Serialize(sample, options), new UTF8Encoding(false));
        }
        catch
        {
            // Not fatal if the sample cannot be written.
        }
    }
}
