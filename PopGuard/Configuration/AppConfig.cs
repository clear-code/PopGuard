namespace PopGuard;

/// <summary>The whole config (rules + duration options).</summary>
internal sealed class AppConfig
{
    public IReadOnlyList<TargetRule> Rules { get; }
    public IReadOnlyList<DurationOption> Durations { get; }
    public bool AutoSuppressDuringFocus { get; }
    public bool AutoSuppressDuringMicrophone { get; }
    public bool ExcludeSystemWindows { get; }

    public AppConfig(
        IReadOnlyList<TargetRule> rules,
        IReadOnlyList<DurationOption> durations,
        bool autoSuppressDuringFocus,
        bool autoSuppressDuringMicrophone,
        bool excludeSystemWindows)
    {
        Rules = rules;
        Durations = durations;
        AutoSuppressDuringFocus = autoSuppressDuringFocus;
        AutoSuppressDuringMicrophone = autoSuppressDuringMicrophone;
        ExcludeSystemWindows = excludeSystemWindows;
    }
}
