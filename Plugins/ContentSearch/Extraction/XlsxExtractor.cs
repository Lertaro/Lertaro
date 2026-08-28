using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace Lertaro.Plugins.ContentSearch.Extraction;

/// <summary>
/// Extracts readable spreadsheet text from Excel (.xlsx) workbooks using built-in ZipArchive and XML parsing.
/// </summary>
public sealed class XlsxExtractor : ITextExtractor
{
    private const int MaxExtractedCharacters = 500_000;
    private static readonly XNamespace SpreadsheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    public bool CanHandle(string extension) =>
        string.Equals(extension, ".xlsx", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(extension, ".xlsm", StringComparison.OrdinalIgnoreCase);

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

                var stringsEntry = archive.GetEntry("xl/sharedStrings.xml");
                if (stringsEntry != null)
                {
                    using var stream = stringsEntry.Open();
                    var xDoc = XDocument.Load(stream);
                    if (xDoc.Root != null)
                    {
                        foreach (var stringItem in xDoc.Descendants(SpreadsheetNs + "si"))
                        {
                            timeoutCts.Token.ThrowIfCancellationRequested();
                            var rowText = new StringBuilder();
                            foreach (var textElem in stringItem.Descendants(SpreadsheetNs + "t"))
                            {
                                rowText.Append(textElem.Value);
                            }

                            if (rowText.Length > 0)
                            {
                                builder.AppendLine(rowText.ToString());
                            }

                            if (builder.Length >= MaxExtractedCharacters)
                                break;
                        }
                    }
                }

                if (builder.Length == 0)
                {
                    var sheetEntries = archive.Entries
                        .Where(e => e.FullName.StartsWith("xl/worksheets/sheet", StringComparison.OrdinalIgnoreCase) &&
                                    e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase));

                    foreach (var entry in sheetEntries)
                    {
                        timeoutCts.Token.ThrowIfCancellationRequested();
                        using var sheetStream = entry.Open();
                        var xDoc = XDocument.Load(sheetStream);
                        if (xDoc.Root == null) continue;

                        foreach (var textElem in xDoc.Descendants(SpreadsheetNs + "t"))
                        {
                            if (!string.IsNullOrWhiteSpace(textElem.Value))
                            {
                                builder.AppendLine(textElem.Value);
                            }

                            if (builder.Length >= MaxExtractedCharacters)
                                break;
                        }

                        if (builder.Length >= MaxExtractedCharacters)
                            break;
                    }
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
