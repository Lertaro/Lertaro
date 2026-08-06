using System.Text.Json;

namespace Lertaro.Core.Services.LocalSend;

/// <summary>Extracts LocalSend desktop show arguments from the optional request body.</summary>
internal static class LocalSendShowRequest
{
    internal static IReadOnlyList<string>? ParseFiles(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return null;

        try
        {
            using var document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty("args", out var args) || args.ValueKind != JsonValueKind.Array)
                return null;
            return args.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()!).ToArray();
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
