using System.Text;
using System.Text.RegularExpressions;

namespace PopGuard;

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
