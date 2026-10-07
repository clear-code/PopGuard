using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using PopGuard.Resources;

namespace PopGuard;

/// <summary>Shape of one config JSON file.</summary>
internal sealed class RuleFile
{
    public List<RuleEntry>? Rules { get; set; }

    // Suppression-duration options shown in the tray menu. Defaults are used when omitted.
    public List<DurationEntry>? Durations { get; set; }

    // Whether to auto-suppress during a Windows 11 focus session (default true).
    public bool? AutoSuppressDuringFocus { get; set; }

    // Whether to auto-suppress while the microphone is in use, e.g. during a call/meeting (default true).
    public bool? AutoSuppressDuringMicrophone { get; set; }

    // Display language: "auto" (default, follow the OS UI language) / "ja" / "en".
    public string? Language { get; set; }
}

/// <summary>One suppression-duration option (JSON). minutes &lt;= 0 means unlimited.</summary>
internal sealed class DurationEntry
{
    // Menu label. May be a plain string (common to all languages) or a language-keyed object
    // like { "ja": "...", "en": "..." }. Omit to auto-generate a localized label from the minutes.
    public LocalizedText? Label { get; set; }

    public int Minutes { get; set; }
}

/// <summary>
/// A piece of UI text given either as a plain string (common to every language) or as a
/// language-keyed object, e.g. <c>{ "ja": "2時間", "en": "2 hours" }</c>.
/// </summary>
[JsonConverter(typeof(LocalizedTextConverter))]
internal sealed class LocalizedText
{
    // Per-language text, keyed by two-letter language code (case-insensitive). Null when a common
    // string was given instead.
    private readonly Dictionary<string, string>? _byLang;
    private readonly string? _common;

    public LocalizedText(string common) => _common = common;

    public LocalizedText(Dictionary<string, string> byLang) => _byLang = byLang;

    /// <summary>Build a Japanese/English pair (used for the sample config).</summary>
    public static LocalizedText Of(string ja, string en) =>
        new(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["ja"] = ja, ["en"] = en });

    /// <summary>
    /// Resolve the text for the current display language. Returns null when nothing applies,
    /// so the caller can fall back (e.g. auto-generate from the minutes).
    /// A language-keyed object only yields text for the language actually present: if just one
    /// language was given, the others get null (and auto-generate) rather than borrowing it.
    /// A plain string is common to every language.
    /// </summary>
    public string? Resolve(bool japanese)
    {
        if (_byLang is { Count: > 0 })
        {
            return _byLang.TryGetValue(japanese ? "ja" : "en", out string? v) && !string.IsNullOrWhiteSpace(v)
                ? v.Trim()
                : null;
        }
        return string.IsNullOrWhiteSpace(_common) ? null : _common!.Trim();
    }

    /// <summary>Serialize back (only needed because the sample config is written out).</summary>
    internal void WriteTo(Utf8JsonWriter writer)
    {
        if (_byLang is { Count: > 0 })
        {
            writer.WriteStartObject();
            foreach (KeyValuePair<string, string> kv in _byLang)
            {
                writer.WriteString(kv.Key, kv.Value);
            }
            writer.WriteEndObject();
        }
        else
        {
            writer.WriteStringValue(_common ?? string.Empty);
        }
    }
}

/// <summary>Reads a <see cref="LocalizedText"/> from either a JSON string or a language-keyed object.</summary>
internal sealed class LocalizedTextConverter : JsonConverter<LocalizedText>
{
    public override LocalizedText? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return null;

            case JsonTokenType.String:
                return new LocalizedText(reader.GetString() ?? string.Empty);

            case JsonTokenType.StartObject:
                var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                while (reader.Read())
                {
                    if (reader.TokenType == JsonTokenType.EndObject)
                    {
                        return new LocalizedText(map);
                    }
                    if (reader.TokenType != JsonTokenType.PropertyName)
                    {
                        throw new JsonException("Unexpected token in localized label object.");
                    }
                    string key = reader.GetString() ?? string.Empty;
                    reader.Read();
                    if (reader.TokenType == JsonTokenType.String)
                    {
                        map[key] = reader.GetString() ?? string.Empty;
                    }
                    else
                    {
                        reader.Skip(); // ignore non-string values defensively
                    }
                }
                throw new JsonException("Unterminated localized label object.");

            default:
                throw new JsonException($"Unexpected token for label: {reader.TokenType}.");
        }
    }

    public override void Write(Utf8JsonWriter writer, LocalizedText value, JsonSerializerOptions options)
        => value.WriteTo(writer);
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
    public bool AutoSuppressDuringMicrophone { get; }

    public AppConfig(
        IReadOnlyList<TargetRule> rules,
        IReadOnlyList<DurationOption> durations,
        bool autoSuppressDuringFocus,
        bool autoSuppressDuringMicrophone)
    {
        Rules = rules;
        Durations = durations;
        AutoSuppressDuringFocus = autoSuppressDuringFocus;
        AutoSuppressDuringMicrophone = autoSuppressDuringMicrophone;
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
/// A wildcard-only process name ("*") means "match any process" (suppress every popup);
/// an empty title/class also means "match anything".
/// </summary>
internal sealed class TargetRule
{
    private readonly Regex? _process;  // null = match any process (e.g. "*")
    private readonly Regex? _title;    // null = no filter
    private readonly Regex? _class;    // null = no filter

    public HideMethod Hide { get; }

    /// <summary>
    /// Whether to target only TOPMOST windows. [Currently unused by PopGuard.]
    /// Parsed so the config is preserved, but not used in matching (always TOPMOST-only).
    /// </summary>
    public bool TopMostOnly { get; }

    private TargetRule(Regex? process, Regex? title, Regex? className, HideMethod hide, bool topMostOnly)
    {
        _process = process;
        _title = title;
        _class = className;
        Hide = hide;
        TopMostOnly = topMostOnly;
    }

    /// <summary>Cheap process-name-only pre-check (to sieve out candidates before the title lookup).</summary>
    public bool MatchesProcess(string process) => _process is null || _process.IsMatch(process ?? string.Empty);

    /// <summary>Match on process, class, and title.</summary>
    public bool Matches(string process, string className, string title)
        => (_process is null || _process.IsMatch(process ?? string.Empty))
           && (_class is null || _class.IsMatch(className ?? string.Empty))
           && (_title is null || _title.IsMatch(title ?? string.Empty));

    /// <summary>
    /// Build a rule from a JSON entry. An empty/whitespace process name is rejected (returns null):
    /// a blank field is treated as a config mistake. A wildcard-only process name ("*") is accepted
    /// and means "match any process" (suppress every popup), compiling to a null process matcher.
    /// </summary>
    public static TargetRule? TryCreate(RuleEntry e, out string? invalidReason)
    {
        invalidReason = null;

        if (string.IsNullOrWhiteSpace(e.Process))
        {
            invalidReason = "process is empty";
            return null;
        }

        // Wildcard.Compile returns null for a wildcard-only pattern ("*"); here that means
        // "match any process" rather than "no filter", because an empty name was already rejected.
        Regex? process = Wildcard.Compile(e.Process);

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
    public static string FilePath => Path.Combine(AppContext.BaseDirectory, "PopGuard.rules.json");

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
            return new AppConfig(new List<TargetRule>(), DefaultDurations(), true, false);
        }

        RuleFile? file;
        try
        {
            file = JsonSerializer.Deserialize<RuleFile>(File.ReadAllText(FilePath), ReadOptions);
        }
        catch (Exception ex)
        {
            Logger.Line("rules: failed to read JSON (nothing will be suppressed): " + ex.Message);
            return new AppConfig(new List<TargetRule>(), DefaultDurations(), true, false);
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

        // Apply the display-language override before building labels that use localized strings.
        Strings.ApplyOverride(file?.Language);

        IReadOnlyList<DurationOption> durations = BuildDurations(file?.Durations);
        bool autoFocus = file?.AutoSuppressDuringFocus ?? true;
        bool autoMic = file?.AutoSuppressDuringMicrophone ?? false;

        Logger.Line($"rules: enabled={rules.Count} invalid={invalid} disabled={disabled} durations={durations.Count} focusSync={autoFocus} micSync={autoMic} path={FilePath}");
        return new AppConfig(rules, durations, autoFocus, autoMic);
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
            list.Add(new DurationOption(ResolveDurationLabel(e), duration));
        }
        return list;
    }

    /// <summary>
    /// Pick a duration label for the current display language: the language-specific text from
    /// <see cref="DurationEntry.Label"/> if present, otherwise an auto-generated (localized) label
    /// from the minutes.
    /// </summary>
    internal static string ResolveDurationLabel(DurationEntry e)
    {
        bool japanese = (Strings.Culture ?? CultureInfo.CurrentUICulture)
            .TwoLetterISOLanguageName.Equals("ja", StringComparison.OrdinalIgnoreCase);

        return e.Label?.Resolve(japanese) is { Length: > 0 } label ? label : AutoLabel(e.Minutes);
    }

    /// <summary>Default options: 30 min / 1 hour / 2 hours / 1 day / unlimited (labels are localized).</summary>
    private static IReadOnlyList<DurationOption> DefaultDurations() => new List<DurationOption>
    {
        new(Strings.Dur30Min, TimeSpan.FromMinutes(30)),
        new(Strings.Dur1Hour, TimeSpan.FromHours(1)),
        new(Strings.Dur2Hours, TimeSpan.FromHours(2)),
        new(Strings.Dur1Day, TimeSpan.FromDays(1)),
        new(Strings.DurUnlimited, null),
    };

    private static string AutoLabel(int minutes)
    {
        if (minutes <= 0)
        {
            return Strings.DurUnlimited;
        }
        if (minutes % 1440 == 0)
        {
            return Strings.DurDays(minutes / 1440);
        }
        if (minutes % 60 == 0)
        {
            return Strings.DurHours(minutes / 60);
        }
        return Strings.DurMinutes(minutes);
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
                // label may be a plain string or a { "ja": ..., "en": ... } object; omit both to
                // auto-generate a localized label from the minutes. Omitting durations entirely
                // falls back to the same defaults.
                Durations = new List<DurationEntry>
                {
                    new() { Label = LocalizedText.Of("30分", "30 min"), Minutes = 30 },
                    new() { Label = LocalizedText.Of("1時間", "1 hour"), Minutes = 60 },
                    new() { Label = LocalizedText.Of("2時間", "2 hours"), Minutes = 120 },
                    new() { Label = LocalizedText.Of("一日", "1 day"), Minutes = 1440 },
                    new() { Label = LocalizedText.Of("無制限", "Unlimited"), Minutes = 0 },
                },
                // Auto-suppress during a Windows 11 focus session (manual Do Not Disturb is not covered).
                AutoSuppressDuringFocus = true,
                // Auto-suppress while the microphone is in use (e.g. during a call/meeting). Default off.
                AutoSuppressDuringMicrophone = false,
                // Display language: "auto" (follow the OS UI language) / "ja" / "en".
                Language = "auto",
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
