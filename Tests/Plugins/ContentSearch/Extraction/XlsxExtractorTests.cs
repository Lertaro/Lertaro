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
                var entry = zip.CreateEntry("xl/sharedStrings.xml");
                using var stream = entry.Open();
                using var writer = new StreamWriter(stream);
                writer.Write("""
                    <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                    <sst xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" count="2" uniqueCount="2">
                        <si><t>Revenue 2026</t></si>
                        <si><t>Projected Growth</t></si>
                    </sst>
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
}
