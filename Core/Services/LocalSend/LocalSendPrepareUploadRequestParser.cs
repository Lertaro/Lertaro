using System.Text.Json;
using Lertaro.Core.Services.LocalSend.Models;

namespace Lertaro.Core.Services.LocalSend;

/// <summary>Validates the required LocalSend prepare-upload fields before deserializing them.</summary>
internal static class LocalSendPrepareUploadRequestParser
{
    internal static bool TryParse(string body, out LocalSendPrepareUploadRequestDto? request)
    {
        request = null;
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !TryGetObject(root, "info", out var info) ||
                !HasString(info, "alias") ||
                !TryGetObject(root, "files", out var files))
                return false;

            foreach (var entry in files.EnumerateObject())
            {
                if (entry.Value.ValueKind != JsonValueKind.Object ||
                    !HasString(entry.Value, "id") ||
                    !HasString(entry.Value, "fileName") ||
                    !HasNonNegativeInt64(entry.Value, "size") ||
                    !HasString(entry.Value, "fileType"))
                    return false;
            }

            request = JsonSerializer.Deserialize<LocalSendPrepareUploadRequestDto>(body);
            return request != null;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryGetObject(JsonElement parent, string name, out JsonElement value) =>
        parent.TryGetProperty(name, out value) && value.ValueKind == JsonValueKind.Object;

    private static bool HasString(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String;

    private static bool HasNonNegativeInt64(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number &&
        value.TryGetInt64(out var parsed) && parsed >= 0;
}
