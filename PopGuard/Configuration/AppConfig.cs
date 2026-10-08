namespace PopGuard;

/// <summary>The whole config (rules + duration options). Built via an object initializer so each
/// value is set by name (the boolean options are easy to transpose positionally).</summary>
internal sealed class AppConfig
{
    public required IReadOnlyList<TargetRule> Rules { get; init; }
    public required IReadOnlyList<DurationOption> Durations { get; init; }
    public bool AutoSuppressDuringFocus { get; init; }
    public bool AutoSuppressDuringMicrophone { get; init; }
    public bool ExcludeSystemWindows { get; init; }
}
