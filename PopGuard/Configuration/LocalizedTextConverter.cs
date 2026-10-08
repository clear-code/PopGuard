using System.Text.Json;
using System.Text.Json.Serialization;

namespace PopGuard;

/// <summary>Reads a <see cref="LocalizedText"/> from either a JSON string or a language-keyed object.</summary>
internal sealed class LocalizedTextConverter : JsonConverter<LocalizedText>
{
    public override LocalizedText? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return null;

            case JsonTokenType.String:
                return new LocalizedText(reader.GetString() ?? string.Empty);

            case JsonTokenType.StartObject:
                var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                while (reader.Read())
                {
                    if (reader.TokenType == JsonTokenType.EndObject)
                    {
                        return new LocalizedText(map);
                    }
                    if (reader.TokenType != JsonTokenType.PropertyName)
                    {
                        throw new JsonException("Unexpected token in localized label object.");
                    }
                    string key = reader.GetString() ?? string.Empty;
                    reader.Read();
                    if (reader.TokenType == JsonTokenType.String)
                    {
                        map[key] = reader.GetString() ?? string.Empty;
                    }
                    else
                    {
                        reader.Skip(); // ignore non-string values defensively
                    }
                }
                throw new JsonException("Unterminated localized label object.");

            default:
                throw new JsonException($"Unexpected token for label: {reader.TokenType}.");
        }
    }

    public override void Write(Utf8JsonWriter writer, LocalizedText value, JsonSerializerOptions options)
        => value.WriteTo(writer);
}
