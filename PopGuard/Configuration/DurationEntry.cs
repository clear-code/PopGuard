namespace PopGuard;

/// <summary>One suppression-duration option (JSON). minutes &lt;= 0 means unlimited.</summary>
internal sealed class DurationEntry
{
    // Menu label. May be a plain string (common to all languages) or a language-keyed object
    // like { "ja": "...", "en": "..." }. Omit to auto-generate a localized label from the minutes.
    public LocalizedText? Label { get; set; }

    public int Minutes { get; set; }
}
