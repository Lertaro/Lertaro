using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace Lertaro.Plugins.ContentSearch.Extraction;

/// <summary>
/// Extracts readable text from Word (.docx) documents using built-in ZipArchive and XML parsing.
/// </summary>
public sealed class DocxExtractor : ITextExtractor
{
    private const int MaxExtractedCharacters = 500_000;
    private static readonly XNamespace WordNs = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    public bool CanHandle(string extension) =>
        string.Equals(extension, ".docx", StringComparison.OrdinalIgnoreCase);

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

                var docEntry = archive.GetEntry("word/document.xml");
                if (docEntry == null)
                    return null;

                using var docStream = docEntry.Open();
                var xDoc = XDocument.Load(docStream);
                if (xDoc.Root == null)
                    return null;

                var builder = new StringBuilder();
                foreach (var paragraph in xDoc.Descendants(WordNs + "p"))
                {
                    timeoutCts.Token.ThrowIfCancellationRequested();
                    var pText = new StringBuilder();
                    foreach (var textElem in paragraph.Descendants(WordNs + "t"))
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

                return builder.Length > 0 ? builder.ToString() : null;
            }, timeoutCts.Token);
        }
        catch
        {
            return null;
        }
    }
}
