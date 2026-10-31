using Lertaro.PluginSdk.Helpers;

namespace Lertaro.Plugins.ContentSearch.Extraction;

/// <summary>
/// Extracts plain text from document and code files. Encoding detection (BOMs, BOM-less UTF-16, strict
/// UTF-8, GB18030 for legacy Chinese files) lives in <see cref="TextEncodingHelper"/>.
/// Binary files with no dedicated extractor (e.g. .doc, .exe typed into the whitelist) are
/// detected and skipped so their bytes never reach the index as mojibake.
/// </summary>
public sealed class PlainTextExtractor : ITextExtractor
{
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

            if (TextEncodingHelper.LooksBinary(bytes))
            {
                PluginSdk.Logger.Log(
                    $"[ContentSearch] Skipped binary file (no dedicated extractor): '{filePath}'",
                    PluginSdk.LogLevel.Warn);
                return null;
            }

            return TextEncodingHelper.DecodeText(bytes);
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
}
