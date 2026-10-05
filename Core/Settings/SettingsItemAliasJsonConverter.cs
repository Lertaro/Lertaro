using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lertaro.Core;

internal sealed class SettingsItemAliasJsonConverter : JsonConverter<Dictionary<string, List<string>>>
{
    public override bool HandleNull => true;

    public override Dictionary<string, List<string>> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        if (document.RootElement.ValueKind == JsonValueKind.Null) return result;

        foreach (var entry in document.RootElement.EnumerateObject())
        {
            var alias = entry.Name.Trim();
            var targets = entry.Value.ValueKind == JsonValueKind.String
                ? new[] { entry.Value.GetString() }
                : entry.Value.EnumerateArray().Select(value => value.GetString());
            foreach (var target in targets)
            {
                if (alias.Length == 0 || string.IsNullOrWhiteSpace(target)) continue;
                if (!result.TryGetValue(alias, out var list)) result[alias] = list = [];
                if (!list.Contains(target.Trim(), StringComparer.OrdinalIgnoreCase)) list.Add(target.Trim());
            }
        }
        return result;
    }

    public override void Write(Utf8JsonWriter writer, Dictionary<string, List<string>> value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        foreach (var entry in value)
        {
            writer.WritePropertyName(entry.Key);
            JsonSerializer.Serialize(writer, entry.Value, options);
        }
        writer.WriteEndObject();
    }
}
