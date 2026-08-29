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
                WriteEntry(zip, "word/document.xml", """
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

    [TestMethod]
    public async Task ExtractTextAsync_SyntheticDocx_ExtractsHeadersFootersNotesAndComments()
    {
        var extractor = new DocxExtractor();
        var tempFile = Path.Combine(Path.GetTempPath(), $"test_docx_{Guid.NewGuid():N}.docx");

        try
        {
            using (var zip = ZipFile.Open(tempFile, ZipArchiveMode.Create))
            {
                WriteEntry(zip, "word/document.xml", """
                    <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                    <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                        <w:body>
                            <w:p><w:r><w:t>Body text with a</w:t><w:tab/><w:t>tabbed tail.</w:t></w:r></w:p>
                        </w:body>
                    </w:document>
                    """);
                WriteEntry(zip, "word/header1.xml", """
                    <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                    <w:hdr xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                        <w:p><w:r><w:t>Header with quarterly numbers.</w:t></w:r></w:p>
                    </w:hdr>
                    """);
                WriteEntry(zip, "word/footer1.xml", """
                    <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                    <w:ftr xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                        <w:p><w:r><w:t>Footer confidentiality notice.</w:t></w:r></w:p>
                    </w:ftr>
                    """);
                WriteEntry(zip, "word/footnotes.xml", """
                    <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                    <w:footnotes xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                        <w:footnote w:id="-1"><w:p><w:r><w:t>separator mark</w:t></w:r></w:p></w:footnote>
                        <w:footnote w:id="0"><w:p><w:r><w:t>continuation mark</w:t></w:r></w:p></w:footnote>
                        <w:footnote w:id="1"><w:p><w:r><w:t>Footnote explains the term.</w:t></w:r></w:p></w:footnote>
                    </w:footnotes>
                    """);
                WriteEntry(zip, "word/comments.xml", """
                    <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                    <w:comments xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                        <w:comment w:id="1" w:author="Reviewer A">
                            <w:p><w:r><w:t>Please rewrite this sentence.</w:t></w:r></w:p>
                        </w:comment>
                    </w:comments>
                    """);
            }

            var text = await extractor.ExtractTextAsync(tempFile, maxFileSizeBytes: 1024 * 1024);

            Assert.IsNotNull(text);
            Assert.Contains("Body text with a", text);
            Assert.Contains("tabbed tail.", text);
            Assert.Contains("Header with quarterly numbers.", text);
            Assert.Contains("Footer confidentiality notice.", text);
            // Real note content is indexed; separator/continuation marks (ids -1 and 0) are not.
            Assert.Contains("Footnote explains the term.", text);
            Assert.DoesNotContain("separator mark", text);
            Assert.DoesNotContain("continuation mark", text);
            Assert.Contains("Reviewer A", text);
            Assert.Contains("Please rewrite this sentence.", text);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    private static void WriteEntry(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream);
        writer.Write(content);
    }
}
