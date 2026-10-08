namespace PopGuard;

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
