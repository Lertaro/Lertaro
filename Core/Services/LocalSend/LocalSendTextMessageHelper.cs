using Lertaro.Core.Services.LocalSend.Models;

namespace Lertaro.Core.Services.LocalSend;

/// <summary>Recognizes the single-file text-message form defined by LocalSend.</summary>
public static class LocalSendTextMessageHelper
{
    public const int MaxPreviewBytes = 16 * 1024;

    internal static bool IsMessageRequest(PrepareUploadRequestDto dto) => dto.Files.Count == 1
        && dto.Files.Values.Single() is { Preview: not null } file && IsTextType(file.FileType);

    public static bool TryGetMessage(PrepareUploadRequestDto dto, out string message)
    {
        message = string.Empty;
        if (!IsMessageRequest(dto)) return false;

        var file = dto.Files.Values.Single();
        if (file.Size > MaxPreviewBytes || file.Preview!.Length > MaxPreviewBytes
            || System.Text.Encoding.UTF8.GetByteCount(file.Preview) > MaxPreviewBytes) return false;

        message = file.Preview;
        return true;
    }

    public static bool TryGetHttpUrl(string text, out Uri? url)
    {
        url = null;
        var candidate = text.Trim();
        if (!candidate.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !candidate.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return false;
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var parsed) ||
            (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps)) return false;
        url = parsed;
        return true;
    }

    private static bool IsTextType(string? fileType) =>
        string.Equals(fileType, "text", StringComparison.OrdinalIgnoreCase) ||
        (!string.IsNullOrEmpty(fileType) && fileType.StartsWith("text/", StringComparison.OrdinalIgnoreCase));
}
