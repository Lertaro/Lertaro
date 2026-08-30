using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace Lertaro.Plugins.ContentSearch.Extraction;

/// <summary>
/// Extracts searchable text from Excel (.xlsx) workbooks using built-in ZipArchive and XML parsing.
/// Text cells are resolved through the shared strings table; numeric cells are read from the
/// sheet's cached values; date-styled cells are normalized to ISO yyyy-MM-dd so date text is searchable.
/// </summary>
public sealed class XlsxExtractor : ITextExtractor
{
    private const int MaxExtractedCharacters = 500_000;
    private static readonly XNamespace SpreadsheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    // Excel serial dates below 61 involve the fictional 1900-02-29 leap day kept for Lotus
    // compatibility; anchoring at 1899-12-30 is exact for every modern date and only mis-dates
    // serials 1-59 by a day, which is irrelevant for search.
    private static readonly DateTime Epoch1900 = new(1899, 12, 30);
    private static readonly DateTime Epoch1904 = new(1904, 1, 1);

    // Built-in number-format ids that render a date; 18-21 and 45-47 render times only.
    private static readonly HashSet<int> BuiltInDateFormats = new() { 14, 15, 16, 17, 22 };

    public bool CanHandle(string extension) =>
        string.Equals(extension, ".xlsx", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(extension, ".xlsm", StringComparison.OrdinalIgnoreCase);

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
                using var archive = new ZipArchive(fileStream, ZipArchiveMode.Read, leaveOpen: false);
                return ExtractWorkbookText(archive, timeoutCts.Token);
            }, timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            PluginSdk.Logger.Log(
                $"[ContentSearch] Timed out extracting workbook '{filePath}'",
                PluginSdk.LogLevel.Warn);
            return null;
        }
        catch (Exception ex)
        {
            PluginSdk.Logger.Log(
                $"[ContentSearch] Failed to extract workbook '{filePath}': {ex.Message}",
                PluginSdk.LogLevel.Warn);
            return null;
        }
    }

    private static string? ExtractWorkbookText(ZipArchive archive, CancellationToken ct)
    {
        var sharedStrings = ReadSharedStrings(archive);
        var (xfNumberFormats, customFormats) = ReadNumberFormats(archive);
        var date1904 = ReadDate1904Flag(archive);

        var builder = new StringBuilder();
        var sheetEntries = archive.Entries
            .Where(e => e.FullName.StartsWith("xl/worksheets/sheet", StringComparison.OrdinalIgnoreCase) &&
                        e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase));

        foreach (var entry in sheetEntries)
        {
            ct.ThrowIfCancellationRequested();
            using var sheetStream = entry.Open();
            var xDoc = XDocument.Load(sheetStream);
            if (xDoc.Root == null) continue;

            foreach (var cell in xDoc.Descendants(SpreadsheetNs + "c"))
            {
                ct.ThrowIfCancellationRequested();
                var cellText = GetCellText(cell, sharedStrings, xfNumberFormats, customFormats, date1904);
                if (string.IsNullOrWhiteSpace(cellText))
                    continue;

                // Flatten in-cell line breaks to spaces (same as dnGrep) so a single cell's
                // phrase stays contiguous for trigram matching.
                builder.AppendLine(cellText.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' '));
                if (builder.Length >= MaxExtractedCharacters)
                    return builder.ToString();
            }
        }

        return builder.Length > 0 ? builder.ToString() : null;
    }

    private static string? GetCellText(
        XElement cell,
        List<string> sharedStrings,
        List<int> xfNumberFormats,
        Dictionary<int, string> customFormats,
        bool date1904)
    {
        var type = (string?)cell.Attribute("t");
        var value = cell.Element(SpreadsheetNs + "v")?.Value;

        switch (type)
        {
            case "s":
                return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index) &&
                       index >= 0 && index < sharedStrings.Count
                    ? sharedStrings[index]
                    : null;
            case "inlineStr":
                return ConcatText(cell.Element(SpreadsheetNs + "is"));
            case "str":
            case "d":
                return value;
            case "b":
                return value == "1" ? "TRUE" : value == "0" ? "FALSE" : null;
            case "e":
                return null;
        }

        // Numeric (t absent or "n"): dates are numbers styled with a date format.
        if (value != null && IsDateStyled(cell, xfNumberFormats, customFormats))
        {
            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var serial)
                ? SerialToIsoDate(serial, date1904)
                : value;
        }

        return value;
    }

    private static bool IsDateStyled(XElement cell, List<int> xfNumberFormats, Dictionary<int, string> customFormats)
    {
        if (!int.TryParse(cell.Attribute("s")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var styleIndex) ||
            styleIndex < 0 || styleIndex >= xfNumberFormats.Count)
        {
            return false;
        }

        return IsDateFormat(xfNumberFormats[styleIndex], customFormats);
    }

    internal static bool IsDateFormat(int numberFormatId, Dictionary<int, string> customFormats)
    {
        if (BuiltInDateFormats.Contains(numberFormatId))
            return true;

        // Custom formats are dates when their code renders y/d/h/s parts (m alone often is a
        // literal or month-only token, so it is not enough on its own).
        return customFormats.TryGetValue(numberFormatId, out var code) &&
               (code.Contains('y') || code.Contains('d') || code.Contains('h') || code.Contains('s'));
    }

    internal static string SerialToIsoDate(double serial, bool date1904)
    {
        var epoch = date1904 ? Epoch1904 : Epoch1900;
        return epoch.AddDays(serial).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private static string? ConcatText(XElement? container)
    {
        if (container == null) return null;

        var builder = new StringBuilder();
        foreach (var textElem in container.Descendants(SpreadsheetNs + "t"))
        {
            builder.Append(textElem.Value);
        }

        return builder.Length > 0 ? builder.ToString() : null;
    }

    private static List<string> ReadSharedStrings(ZipArchive archive)
    {
        var shared = new List<string>();
        var entry = archive.GetEntry("xl/sharedStrings.xml");
        if (entry == null) return shared;

        using var stream = entry.Open();
        var xDoc = XDocument.Load(stream);
        if (xDoc.Root == null) return shared;

        foreach (var item in xDoc.Descendants(SpreadsheetNs + "si"))
        {
            var text = ConcatText(item);
            shared.Add(text ?? string.Empty);
        }

        return shared;
    }

    private static (List<int> XfNumberFormats, Dictionary<int, string> CustomFormats) ReadNumberFormats(ZipArchive archive)
    {
        var xfNumberFormats = new List<int>();
        var customFormats = new Dictionary<int, string>();
        var entry = archive.GetEntry("xl/styles.xml");
        if (entry == null) return (xfNumberFormats, customFormats);

        using var stream = entry.Open();
        var xDoc = XDocument.Load(stream);
        if (xDoc.Root == null) return (xfNumberFormats, customFormats);

        foreach (var format in xDoc.Descendants(SpreadsheetNs + "numFmt"))
        {
            if (int.TryParse(format.Attribute("numFmtId")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) &&
                format.Attribute("formatCode")?.Value is { } code)
            {
                customFormats[id] = code;
            }
        }

        // Cell style indexes reference cellXfs only; cellStyleXfs also contains xf elements
        // and must be excluded or every index after it would shift.
        foreach (var xf in xDoc.Descendants(SpreadsheetNs + "cellXfs").Descendants(SpreadsheetNs + "xf"))
        {
            xfNumberFormats.Add(
                int.TryParse(xf.Attribute("numFmtId")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : 0);
        }

        return (xfNumberFormats, customFormats);
    }

    private static bool ReadDate1904Flag(ZipArchive archive)
    {
        var entry = archive.GetEntry("xl/workbook.xml");
        if (entry == null) return false;

        using var stream = entry.Open();
        var xDoc = XDocument.Load(stream);
        return (string?)xDoc.Root?.Element(SpreadsheetNs + "workbookPr")?.Attribute("date1904") == "1";
    }
}
