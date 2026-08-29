using System.Globalization;
using System.IO.Compression;
using Lertaro.Plugins.ContentSearch.Extraction;

namespace Lertaro.Plugins.ContentSearch.Tests.Extraction;

[TestClass]
public sealed class XlsxExtractorTests
{
    [TestMethod]
    public void CanHandle_XlsxExtension_ReturnsTrue()
    {
        var extractor = new XlsxExtractor();
        Assert.IsTrue(extractor.CanHandle(".xlsx"));
        Assert.IsTrue(extractor.CanHandle(".XLSX"));
        Assert.IsTrue(extractor.CanHandle(".xlsm"));
        Assert.IsFalse(extractor.CanHandle(".xls"));
        Assert.IsFalse(extractor.CanHandle(".txt"));
    }

    [TestMethod]
    public async Task ExtractTextAsync_SyntheticXlsx_ExtractsSharedStrings()
    {
        var extractor = new XlsxExtractor();
        var tempFile = Path.Combine(Path.GetTempPath(), $"test_xlsx_{Guid.NewGuid():N}.xlsx");

        try
        {
            using (var zip = ZipFile.Open(tempFile, ZipArchiveMode.Create))
            {
                WriteEntry(zip, "xl/sharedStrings.xml", """
                    <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                    <sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" count="2" uniqueCount="2">
                        <si><t>Revenue 2026</t></si>
                        <si><t>Projected Growth</t></si>
                    </sst>
                    """);
                WriteEntry(zip, "xl/worksheets/sheet1.xml", """
                    <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                    <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
                        <sheetData>
                            <row r="1"><c r="A1" t="s"><v>0</v></c><c r="B1" t="s"><v>1</v></c></row>
                        </sheetData>
                    </worksheet>
                    """);
            }

            var text = await extractor.ExtractTextAsync(tempFile, maxFileSizeBytes: 1024 * 1024);

            Assert.IsNotNull(text);
            Assert.Contains("Revenue 2026", text);
            Assert.Contains("Projected Growth", text);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [TestMethod]
    public async Task ExtractTextAsync_SyntheticXlsx_ExtractsNumbersDatesAndInlineStrings()
    {
        var extractor = new XlsxExtractor();
        var tempFile = Path.Combine(Path.GetTempPath(), $"test_xlsx_{Guid.NewGuid():N}.xlsx");

        try
        {
            using (var zip = ZipFile.Open(tempFile, ZipArchiveMode.Create))
            {
                WriteEntry(zip, "xl/sharedStrings.xml", """
                    <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                    <sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" count="1" uniqueCount="1">
                        <si><t>Revenue 2026</t></si>
                    </sst>
                    """);
                // cellStyleXfs holds one xf that must not shift the cellXfs indexes used by cell s attributes.
                WriteEntry(zip, "xl/styles.xml", """
                    <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                    <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
                        <cellStyleXfs count="1"><xf numFmtId="0"/></cellStyleXfs>
                        <cellXfs count="3">
                            <xf numFmtId="14"/>
                            <xf numFmtId="0"/>
                            <xf numFmtId="0"/>
                        </cellXfs>
                    </styleSheet>
                    """);
                WriteEntry(zip, "xl/worksheets/sheet1.xml", """
                    <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                    <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
                        <sheetData>
                            <row r="1"><c r="A1" t="s"><v>0</v></c></row>
                            <row r="2"><c r="A2"><v>12345.6</v></c></row>
                            <row r="3"><c r="A3" s="0"><v>45123</v></c></row>
                            <row r="4"><c r="A4" s="1"><v>99</v></c></row>
                            <row r="5"><c r="A5" t="inlineStr"><is><t>Note inline</t></is></c></row>
                            <row r="6"><c r="A6" t="b"><v>1</v></c></row>
                        </sheetData>
                    </worksheet>
                    """);
            }

            var text = await extractor.ExtractTextAsync(tempFile, maxFileSizeBytes: 1024 * 1024);

            Assert.IsNotNull(text);
            Assert.Contains("Revenue 2026", text);
            Assert.Contains("12345.6", text);
            // Date-styled cell (numFmtId 14) is normalized to ISO; non-date number stays numeric.
            var expectedDate = new DateTime(1899, 12, 30).AddDays(45123).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            Assert.Contains(expectedDate, text);
            // The date-styled cell must not surface as its raw serial number.
            Assert.DoesNotContain("45123", text);
            Assert.Contains("99", text);
            Assert.Contains("Note inline", text);
            Assert.Contains("TRUE", text);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [TestMethod]
    public void IsDateFormat_BuiltInAndCustomCodes()
    {
        var custom = new Dictionary<int, string> { { 164, "yyyy\\-mm\\-dd" }, { 165, "0.00" } };

        Assert.IsTrue(XlsxExtractor.IsDateFormat(14, custom));
        Assert.IsTrue(XlsxExtractor.IsDateFormat(22, custom));
        Assert.IsTrue(XlsxExtractor.IsDateFormat(164, custom));
        Assert.IsFalse(XlsxExtractor.IsDateFormat(0, custom));
        Assert.IsFalse(XlsxExtractor.IsDateFormat(165, custom));
        Assert.IsFalse(XlsxExtractor.IsDateFormat(999, custom));
    }

    [TestMethod]
    public void SerialToIsoDate_RespectsDateSystem()
    {
        // Serial 44927 is 2023-01-01 in the default 1900 date system.
        Assert.AreEqual("2023-01-01", XlsxExtractor.SerialToIsoDate(44927, date1904: false));
        // The 1904 system drops the two fictional 1900 leap-day serials, so the same
        // calendar date sits 1462 serials earlier.
        Assert.AreEqual("2023-01-01", XlsxExtractor.SerialToIsoDate(43465, date1904: true));
    }

    private static void WriteEntry(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream);
        writer.Write(content);
    }
}
