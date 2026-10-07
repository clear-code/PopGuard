using System.Globalization;
using System.Resources;

namespace PopGuard.Resources;

/// <summary>
/// Typed accessor for the localized display strings in Strings.resx / Strings.ja.resx.
/// English is the neutral (default); Japanese is used when the UI culture is ja (e.g. ja-JP).
/// </summary>
internal static class Strings
{
    private static readonly ResourceManager Rm =
        new("PopGuard.Resources.Strings", typeof(Strings).Assembly);

    /// <summary>Override the display language. null = follow CurrentUICulture.</summary>
    public static CultureInfo? Culture { get; set; }

    private static string Get(string key) => Rm.GetString(key, Culture) ?? key;

    private static string Fmt(string key, params object[] args)
        => string.Format(CultureInfo.CurrentCulture, Get(key), args);

    // Tray menu
    public static string MenuSuppress => Get(nameof(MenuSuppress));
    public static string MenuStop => Get(nameof(MenuStop));
    public static string MenuExit => Get(nameof(MenuExit));

    // Status line
    public static string StatusPrefix => Get(nameof(StatusPrefix));
    public static string StatusNotSuppressing => Get(nameof(StatusNotSuppressing));
    public static string StatusUnlimited => Get(nameof(StatusUnlimited));
    public static string StatusFocus => Get(nameof(StatusFocus));

    // Read-only setting display (focus / microphone auto-suppress)
    public static string SyncFocus => Get(nameof(SyncFocus));
    public static string SyncMic => Get(nameof(SyncMic));
    public static string StateEnabled => Get(nameof(StateEnabled));
    public static string StateDisabled => Get(nameof(StateDisabled));
    public static string StateUnavailable => Get(nameof(StateUnavailable));
    public static string StatusMic => Get(nameof(StatusMic));
    public static string StatusAuto => Get(nameof(StatusAuto));
    public static string StatusRemaining(string remaining) => Fmt(nameof(StatusRemaining), remaining);

    // Balloon
    public static string BalloonSuppress(string label) => Fmt(nameof(BalloonSuppress), label);

    // Duration labels
    public static string DurUnlimited => Get(nameof(DurUnlimited));
    public static string Dur30Min => Get(nameof(Dur30Min));
    public static string Dur1Hour => Get(nameof(Dur1Hour));
    public static string Dur2Hours => Get(nameof(Dur2Hours));
    public static string Dur1Day => Get(nameof(Dur1Day));
    public static string DurMinutes(int n) => Fmt("DurMinutesFmt", n);
    public static string DurHours(int n) => Fmt("DurHoursFmt", n);
    public static string DurDays(int n) => Fmt("DurDaysFmt", n);

    /// <summary>Apply a language override from config: "ja" / "en" / "auto" (or null = follow OS).</summary>
    public static void ApplyOverride(string? language)
    {
        Culture = language?.Trim().ToLowerInvariant() switch
        {
            "ja" => new CultureInfo("ja"),
            "en" => new CultureInfo("en"),
            _ => null, // "auto", null, or unknown -> follow CurrentUICulture
        };
    }
}
