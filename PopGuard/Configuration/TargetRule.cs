using System.Text.RegularExpressions;

namespace PopGuard;

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

    /// <summary>When true this is an exclusion rule: matching windows are never suppressed.</summary>
    public bool Exclude { get; }

    /// <summary>
    /// Whether to target only TOPMOST windows. [Currently unused by PopGuard.]
    /// Parsed so the config is preserved, but not used in matching (always TOPMOST-only).
    /// </summary>
    public bool TopMostOnly { get; }

    private TargetRule(Regex? process, Regex? title, Regex? className, HideMethod hide, bool exclude, bool topMostOnly)
    {
        _process = process;
        _title = title;
        _class = className;
        Hide = hide;
        Exclude = exclude;
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

        return new TargetRule(process, title, className, hide, e.Exclude, e.TopMostOnly);
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
