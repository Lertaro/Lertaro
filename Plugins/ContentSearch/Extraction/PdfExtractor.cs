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
                var failedPages = 0;

                // ponytail: the give-up threshold is a heuristic, not tuned from data. A
                // document where most pages fail to parse yields garbage indexing value for
                // the same CPU cost as a good one; if real documents start tripping this
                // with recoverable damage, raise the floor (3) first.
                var giveUpAfterFailedPages = Math.Max(3, document.NumberOfPages / 2);

                for (var pageNumber = 1; pageNumber <= document.NumberOfPages; pageNumber++)
                {
                    timeoutCts.Token.ThrowIfCancellationRequested();

                    // Some PDFs draw glyphs on a slightly rotated text matrix (e.g. 4 degrees);
                    // PdfPig 0.1.9 threw from Letter.GetTextOrientationRot while processing such
                    // a page (fixed upstream in 0.1.10). One bad page must not void the whole
                    // document, so pages that fail are counted, not logged individually: if too
                    // many fail, the document is given up with a single summary warning.
                    string? pageText;
                    try
                    {
                        pageText = document.GetPage(pageNumber).Text;
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch
                    {
                        failedPages++;
                        if (failedPages >= giveUpAfterFailedPages)
                        {
                            PluginSdk.Logger.Log(
                                $"[ContentSearch] Giving up on PDF '{filePath}': {failedPages} of {document.NumberOfPages} pages failed to process",
                                PluginSdk.LogLevel.Warn);
                            return null;
                        }
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(pageText))
                    {
                        builder.AppendLine(pageText);
                    }

                    if (pageNumber >= MaxPagesToExtract || builder.Length >= MaxExtractedCharacters)
                        break;
                }

                if (failedPages > 0)
                {
                    PluginSdk.Logger.Log(
                        $"[ContentSearch] PDF '{filePath}' indexed with {failedPages} unreadable page(s) skipped",
                        PluginSdk.LogLevel.Info);
                }

                // Empty string (not null) when the document opens fine but has no text on any
                // page (image-only PDF): lets the scheduler log a distinct "no extractable
                // text" warning, while null stays reserved for actual extraction failures.
                return builder.ToString();
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
