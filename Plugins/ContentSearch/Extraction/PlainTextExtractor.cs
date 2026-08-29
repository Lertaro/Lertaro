using System.Text;

namespace Lertaro.Plugins.ContentSearch.Extraction;

/// <summary>
/// Extracts plain text from document and code files, honoring BOMs and falling back to
/// GB18030 (superset of GBK/GB2312) for legacy Chinese-encoded files.
/// </summary>
public sealed class PlainTextExtractor : ITextExtractor
{
    static PlainTextExtractor() =>
        // GB18030 is not in the default .NET Core encoding set; the code pages provider
        // ships in the shared framework and only needs registering once per process.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public bool CanHandle(string extension) => !string.IsNullOrWhiteSpace(extension);

    public async Task<string?> ExtractTextAsync(string filePath, long maxFileSizeBytes, CancellationToken cancellationToken = default)
    {
        try
        {
            var fileInfo = new FileInfo(filePath);
            if (!fileInfo.Exists || fileInfo.Length > maxFileSizeBytes || fileInfo.Length == 0)
                return null;

            var bytes = await File.ReadAllBytesAsync(filePath, cancellationToken);
            return DecodeText(bytes);
        }
        catch
        {
            return null;
        }
    }

    internal static string DecodeText(byte[] bytes)
    {
        if (bytes.Length == 0) return string.Empty;

        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return new UTF8Encoding(false).GetString(bytes, 3, bytes.Length - 3);
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);

        try
        {
            // Strict UTF-8 first: a UTF-8 file must never be misread as GB18030, while a
            // GBK file virtually always contains byte sequences that are invalid UTF-8.
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.GetEncoding("GB18030").GetString(bytes);
        }
    }
}
