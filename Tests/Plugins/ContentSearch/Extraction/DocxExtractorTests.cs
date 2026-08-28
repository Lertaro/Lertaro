using System.IO.Compression;
using Lertaro.Plugins.ContentSearch.Extraction;

namespace Lertaro.Plugins.ContentSearch.Tests.Extraction;

[TestClass]
public sealed class DocxExtractorTests
{
    [TestMethod]
    public void CanHandle_DocxExtension_ReturnsTrue()
    {
        var extractor = new DocxExtractor();
        Assert.IsTrue(extractor.CanHandle(".docx"));
        Assert.IsTrue(extractor.CanHandle(".DOCX"));
        Assert.IsFalse(extractor.CanHandle(".doc"));
        Assert.IsFalse(extractor.CanHandle(".txt"));
    }

    [TestMethod]
    public async Task ExtractTextAsync_SyntheticDocx_ExtractsParagraphs()
    {
        var extractor = new DocxExtractor();
        var tempFile = Path.Combine(Path.GetTempPath(), $"test_docx_{Guid.NewGuid():N}.docx");

        try
        {
            using (var zip = ZipFile.Open(tempFile, ZipArchiveMode.Create))
            {
                var entry = zip.CreateEntry("word/document.xml");
                using var stream = entry.Open();
                using var writer = new StreamWriter(stream);
                writer.Write("""
                    <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                    <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                        <w:body>
                            <w:p><w:r><w:t>First test paragraph.</w:t></w:r></w:p>
                            <w:p><w:r><w:t>Second test paragraph.</w:t></w:r></w:p>
                        </w:body>
                    </w:document>
                    """);
            }

            var text = await extractor.ExtractTextAsync(tempFile, maxFileSizeBytes: 1024 * 1024);

            Assert.IsNotNull(text);
            Assert.Contains("First test paragraph.", text);
            Assert.Contains("Second test paragraph.", text);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }
}
