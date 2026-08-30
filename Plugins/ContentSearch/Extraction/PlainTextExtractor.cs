using System.Text;

namespace Lertaro.Plugins.ContentSearch.Extraction;

/// <summary>
/// Extracts plain text from document and code files, honoring BOMs and falling back to
/// GB18030 (superset of GBK/GB2312) for legacy Chinese-encoded files.
/// Binary files with no dedicated extractor (e.g. .doc, .exe typed into the whitelist) are
/// detected and skipped so their bytes never reach the index as mojibake.
/// </summary>
public sealed class PlainTextExtractor : ITextExtractor
{
    // A NUL byte in the leading chunk is the standard binary tell (git uses the same):
    // real text never contains one, while OLE (.doc/.xls) and PE (.exe) headers have many.
    // BOM'd UTF-16 is exempted first since its text is NUL-interleaved.
    private const int BinaryProbeLength = 8192;

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

            // Reading is I/O-bound but can still hang on network shares; apply the same
            // size-proportional timeout as the CPU-bound extractors.
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(ExtractorTimeoutPolicy.ForFileSize(fileInfo.Length));

            var bytes = await File.ReadAllBytesAsync(filePath, timeoutCts.Token);

            if (!HasUnicodeBom(bytes) && LooksBinary(bytes))
            {
                PluginSdk.Logger.Log(
                    $"[ContentSearch] Skipped binary file (no dedicated extractor): '{filePath}'",
                    PluginSdk.LogLevel.Warn);
                return null;
            }

            return DecodeText(bytes);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            PluginSdk.Logger.Log(
                $"[ContentSearch] Timed out extracting text from '{filePath}'",
                PluginSdk.LogLevel.Warn);
            return null;
        }
        catch (Exception ex)
        {
            PluginSdk.Logger.Log(
                $"[ContentSearch] Failed to extract text from '{filePath}': {ex.Message}",
                PluginSdk.LogLevel.Warn);
            return null;
        }
    }

    internal static bool LooksBinary(byte[] bytes) =>
        Array.IndexOf(bytes, (byte)0, 0, Math.Min(bytes.Length, BinaryProbeLength)) >= 0;

    internal static bool HasUnicodeBom(ReadOnlySpan<byte> bytes) =>
        (bytes.Length >= 2 && ((bytes[0] == 0xFF && bytes[1] == 0xFE) || (bytes[0] == 0xFE && bytes[1] == 0xFF))) ||
        (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);

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
