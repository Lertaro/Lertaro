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
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(5));

        try
        {
            return await Task.Run(() =>
            {
                timeoutCts.Token.ThrowIfCancellationRequested();
                using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var document = PdfDocument.Open(fileStream);

                var builder = new StringBuilder();
                var pageCount = 0;

                foreach (var page in document.GetPages())
                {
                    timeoutCts.Token.ThrowIfCancellationRequested();
                    if (!string.IsNullOrWhiteSpace(page.Text))
                    {
                        builder.AppendLine(page.Text);
                    }

                    if (++pageCount >= MaxPagesToExtract || builder.Length >= MaxExtractedCharacters)
                        break;
                }

                return builder.Length > 0 ? builder.ToString() : null;
            }, timeoutCts.Token);
        }
        catch
        {
            return null;
        }
    }
}
