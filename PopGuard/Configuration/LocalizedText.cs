using System.Text.Json;
using System.Text.Json.Serialization;

namespace PopGuard;

/// <summary>
/// A piece of UI text given either as a plain string (common to every language) or as a
/// language-keyed object, e.g. <c>{ "ja": "2時間", "en": "2 hours" }</c>.
/// </summary>
[JsonConverter(typeof(LocalizedTextConverter))]
internal sealed class LocalizedText
{
    // Per-language text, keyed by two-letter language code (case-insensitive). Null when a common
    // string was given instead.
    private readonly Dictionary<string, string>? _byLang;
    private readonly string? _common;

    public LocalizedText(string common) => _common = common;

    public LocalizedText(Dictionary<string, string> byLang) => _byLang = byLang;

    /// <summary>Build a Japanese/English pair (used for the sample config).</summary>
    public static LocalizedText Of(string ja, string en) =>
        new(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["ja"] = ja, ["en"] = en });

    /// <summary>
    /// Resolve the text for the current display language. Returns null when nothing applies,
    /// so the caller can fall back (e.g. auto-generate from the minutes).
    /// A language-keyed object only yields text for the language actually present: if just one
    /// language was given, the others get null (and auto-generate) rather than borrowing it.
    /// A plain string is common to every language.
    /// </summary>
    public string? Resolve(bool japanese)
    {
        if (_byLang is { Count: > 0 })
        {
            return _byLang.TryGetValue(japanese ? "ja" : "en", out string? v) && !string.IsNullOrWhiteSpace(v)
                ? v.Trim()
                : null;
        }
        return string.IsNullOrWhiteSpace(_common) ? null : _common!.Trim();
    }

    /// <summary>Serialize back (only needed because the sample config is written out).</summary>
    internal void WriteTo(Utf8JsonWriter writer)
    {
        if (_byLang is { Count: > 0 })
        {
            writer.WriteStartObject();
            foreach (KeyValuePair<string, string> kv in _byLang)
            {
                writer.WriteString(kv.Key, kv.Value);
            }
            writer.WriteEndObject();
        }
        else
        {
            writer.WriteStringValue(_common ?? string.Empty);
        }
    }
}
