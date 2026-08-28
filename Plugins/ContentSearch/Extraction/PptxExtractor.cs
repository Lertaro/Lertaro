using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace Lertaro.Plugins.ContentSearch.Extraction;

/// <summary>
/// Extracts readable slide text from PowerPoint (.pptx) presentations using built-in ZipArchive and DrawingML parsing.
/// </summary>
public sealed class PptxExtractor : ITextExtractor
{
    private const int MaxExtractedCharacters = 500_000;
    private static readonly XNamespace DrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";

    public bool CanHandle(string extension) =>
        string.Equals(extension, ".pptx", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(extension, ".pptm", StringComparison.OrdinalIgnoreCase);

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
                using var archive = new ZipArchive(fileStream, ZipArchiveMode.Read, leaveOpen: false);

                var builder = new StringBuilder();

                var slideEntries = archive.Entries
                    .Where(e => e.FullName.StartsWith("ppt/slides/slide", StringComparison.OrdinalIgnoreCase) &&
                                e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(e => e.FullName, StringComparer.OrdinalIgnoreCase);

                foreach (var entry in slideEntries)
                {
                    timeoutCts.Token.ThrowIfCancellationRequested();
                    using var slideStream = entry.Open();
                    var xDoc = XDocument.Load(slideStream);
                    if (xDoc.Root == null) continue;

                    foreach (var paragraph in xDoc.Descendants(DrawingNs + "p"))
                    {
                        var pText = new StringBuilder();
                        foreach (var textElem in paragraph.Descendants(DrawingNs + "t"))
                        {
                            pText.Append(textElem.Value);
                        }

                        if (pText.Length > 0)
                        {
                            builder.AppendLine(pText.ToString());
                        }

                        if (builder.Length >= MaxExtractedCharacters)
                            break;
                    }

                    if (builder.Length >= MaxExtractedCharacters)
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
