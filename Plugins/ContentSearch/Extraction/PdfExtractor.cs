using System.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.AcroForms;

namespace Lertaro.Plugins.ContentSearch.Extraction;

/// <summary>
/// Extracts readable text from PDF documents using PdfPig.
/// </summary>
public sealed class PdfExtractor : ITextExtractor
{
    private const int MaxPagesToExtract = 150;
    private const int MaxExtractedCharacters = 500_000;

    // An unbroken run of this many unparseable pages gives up on the whole document:
    // a long failing run predicts the rest fails the same way, even when the overall
    // failure ratio stays below the give-up threshold (e.g. a 1000-page PDF whose
    // first 8 pages are all broken).
    private const int MaxConsecutiveFailedPages = 8;

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
                var consecutiveFailedPages = 0;

                // ponytail: the give-up thresholds are heuristics, not tuned from data. A
                // document where most pages fail to parse yields garbage indexing value for
                // the same CPU cost as a good one; if real documents start tripping this
                // with recoverable damage, raise the floor (3) and the run length (8) first.
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
                        consecutiveFailedPages++;

                        // Two give-up signals: the document is mostly broken overall, or an
                        // unbroken run of failures means the tail of a large document fails
                        // the same way its recent past did and is not worth scanning.
                        if (consecutiveFailedPages >= MaxConsecutiveFailedPages || failedPages >= giveUpAfterFailedPages)
                        {
                            PluginSdk.Logger.Log(
                                $"[ContentSearch] Giving up on PDF '{filePath}': {failedPages} of {document.NumberOfPages} pages failed to process",
                                PluginSdk.LogLevel.Warn);
                            return null;
                        }
                        continue;
                    }

                    consecutiveFailedPages = 0;

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

                AppendFormFieldValues(document, filePath, builder);

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

    /// <summary>
    /// Appends saved AcroForm field values (name: value lines) after the page text. The
    /// filled-in content of interactive PDFs such as invoices lives in field values, not
    /// in the page content stream, so a text-only extraction would miss it entirely.
    /// </summary>
    private static void AppendFormFieldValues(PdfDocument document, string filePath, StringBuilder builder)
    {
        // ponytail: XFA-only forms (Adobe's XML Forms Architecture) are not supported by
        // PdfPig; the xpdf parser has preferXFAFieldValues for that case but is GPL and
        // cannot be embedded. Saved AcroForm values cover the common fillable invoice.
        try
        {
            if (!document.TryGetForm(out var form) || form is null)
                return;

            foreach (var field in form.GetFields())
            {
                // GetFieldValue's key is the fully qualified field name, its value the
                // saved contents (empty for untouched fields).
                var fieldValue = field.GetFieldValue();
                if (string.IsNullOrWhiteSpace(fieldValue.Value))
                    continue;

                builder.AppendLine($"{fieldValue.Key}: {fieldValue.Value}");
            }
        }
        catch (Exception ex)
        {
            // A broken form dictionary must not void an otherwise extracted document.
            PluginSdk.Logger.Log(
                $"[ContentSearch] Could not read form fields of '{filePath}': {ex.Message}",
                PluginSdk.LogLevel.Info);
        }
    }
}
