using System.Text;
using UglyToad.PdfPig;

namespace Lertaro.Plugins.ContentSearch.Extraction;

/// <summary>
/// Extracts readable text from PDF documents using PdfPig.
/// </summary>
public sealed class PdfExtractor : ITextExtractor
{
    private const int MaxPagesToExtract = 150;
    private const int MaxExtractedCharacters = 500_000;

    public bool CanHandle(string extension) =>
        string.Equals(extension, ".pdf", StringComparison.OrdinalIgnoreCase);

    public async Task<string?> ExtractTextAsync(string filePath, long maxFileSizeBytes, CancellationToken cancellationToken = default)
    {
        var fileInfo = new FileInfo(filePath);
        if (!fileInfo.Exists || fileInfo.Length > maxFileSizeBytes || fileInfo.Length == 0)
            return null;

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(ExtractorTimeoutPolicy.ForFileSize(fileInfo.Length));

        try
        {
            return await Task.Run(() =>
            {
                timeoutCts.Token.ThrowIfCancellationRequested();
                using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var document = PdfDocument.Open(fileStream);

                var builder = new StringBuilder();
                for (var pageNumber = 1; pageNumber <= document.NumberOfPages; pageNumber++)
                {
                    timeoutCts.Token.ThrowIfCancellationRequested();

                    // Some PDFs draw glyphs on a slightly rotated text matrix (e.g. 4 degrees);
                    // PdfPig 0.1.9 throws from Letter.GetTextOrientationRot while processing such
                    // a page. One bad page must not void the whole document, so isolate each page
                    // and skip the ones that fail to process.
                    string? pageText;
                    try
                    {
                        pageText = document.GetPage(pageNumber).Text;
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        PluginSdk.Logger.Log(
                            $"[ContentSearch] PDF page {pageNumber} of '{filePath}' failed to process, skipping: {ex.Message}",
                            PluginSdk.LogLevel.Warn);
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(pageText))
                    {
                        builder.AppendLine(pageText);
                    }

                    if (pageNumber >= MaxPagesToExtract || builder.Length >= MaxExtractedCharacters)
                        break;
                }

                return builder.Length > 0 ? builder.ToString() : null;
            }, timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            PluginSdk.Logger.Log(
                $"[ContentSearch] Timed out extracting PDF '{filePath}'",
                PluginSdk.LogLevel.Warn);
            return null;
        }
        catch (Exception ex)
        {
            PluginSdk.Logger.Log(
                $"[ContentSearch] Failed to extract PDF '{filePath}': {ex.Message}",
                PluginSdk.LogLevel.Warn);
            return null;
        }
    }
}
