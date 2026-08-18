using Lertaro.Core.Services.LocalSend.Models;

namespace Lertaro.Core.Services.LocalSend;

/// <summary>Recognizes the single-file text-message form defined by LocalSend.</summary>
public static class LocalSendTextMessageHelper
{
    public static bool TryGetMessage(PrepareUploadRequestDto dto, out string message)
    {
        message = string.Empty;
        if (dto.Files.Count != 1) return false;

        var file = dto.Files.Values.Single();
        if (file.Preview == null || !IsTextType(file.FileType)) return false;

        message = file.Preview;
        return true;
    }

    private static bool IsTextType(string? fileType) =>
        string.Equals(fileType, "text", StringComparison.OrdinalIgnoreCase) ||
        (!string.IsNullOrEmpty(fileType) && fileType.StartsWith("text/", StringComparison.OrdinalIgnoreCase));
}
