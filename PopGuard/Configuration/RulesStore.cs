using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using PopGuard.Resources;

namespace PopGuard;

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
            return new AppConfig(new List<TargetRule>(), DefaultDurations(), true, false, true);
        }

        RuleFile? file;
        try
        {
            file = JsonSerializer.Deserialize<RuleFile>(File.ReadAllText(FilePath), ReadOptions);
        }
        catch (Exception ex)
        {
            Logger.Line("rules: failed to read JSON (nothing will be suppressed): " + ex.Message);
            return new AppConfig(new List<TargetRule>(), DefaultDurations(), true, false, true);
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
        bool excludeSystem = file?.ExcludeSystemWindows ?? true;

        Logger.Line($"rules: enabled={rules.Count} invalid={invalid} disabled={disabled} durations={durations.Count} focusSync={autoFocus} micSync={autoMic} excludeSystem={excludeSystem} path={FilePath}");
        return new AppConfig(rules, durations, autoFocus, autoMic, excludeSystem);
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
                    // Exclusion example: never suppress a window with this title, even if a later rule
                    // would match it. Place exclude rules ABOVE broader rules (first match wins).
                    new()
                    {
                        Enabled = false,
                        Process = "SomeNotifier",
                        Title = "*Important*",
                        Exclude = true,
                    },
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
                // Always exclude Windows shell windows (taskbar, desktop, …) from suppression.
                ExcludeSystemWindows = true,
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
