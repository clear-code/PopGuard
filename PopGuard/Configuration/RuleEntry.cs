namespace PopGuard;

/// <summary>Shape of one rule in JSON (raw strings).</summary>
internal sealed class RuleEntry
{
    public bool Enabled { get; set; } = true;
    public string? Process { get; set; }
    public string? Title { get; set; }
    public string? Class { get; set; }
    public string? Hide { get; set; }

    // When true, windows matching this rule are NEVER suppressed ("allow"/exclusion rule).
    // Because the first matching rule wins, place an exclude rule above broader rules to carve out
    // exceptions (e.g. exclude a specific title, then suppress everything else with process "*").
    public bool Exclude { get; set; }

    // Whether to target only topmost (TOPMOST) windows.
    // [Currently not implemented] accepted as config, but internally always TOPMOST-only.
    // Kept to leave room for implementing false (also target non-TOPMOST) later.
    public bool TopMostOnly { get; set; } = true;
}
