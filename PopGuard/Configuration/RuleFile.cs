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

    // Whether to always exclude Windows shell windows (taskbar, desktop, …) from suppression (default true).
    public bool? ExcludeSystemWindows { get; set; }

    // Display language: "auto" (default, follow the OS UI language) / "ja" / "en".
    public string? Language { get; set; }
}
