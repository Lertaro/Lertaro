using System.IO.Compression;
using Lertaro.Plugins.ContentSearch.Extraction;

namespace Lertaro.Plugins.ContentSearch.Tests.Extraction;

[TestClass]
public sealed class PptxExtractorTests
{
    [TestMethod]
    public void CanHandle_PptxExtension_ReturnsTrue()
    {
        var extractor = new PptxExtractor();
        Assert.IsTrue(extractor.CanHandle(".pptx"));
        Assert.IsTrue(extractor.CanHandle(".PPTX"));
        Assert.IsTrue(extractor.CanHandle(".pptm"));
        Assert.IsFalse(extractor.CanHandle(".ppt"));
        Assert.IsFalse(extractor.CanHandle(".txt"));
    }

    [TestMethod]
    public async Task ExtractTextAsync_SyntheticPptx_ExtractsSlideText()
    {
        var extractor = new PptxExtractor();
        var tempFile = Path.Combine(Path.GetTempPath(), $"test_pptx_{Guid.NewGuid():N}.pptx");

        try
        {
            using (var zip = ZipFile.Open(tempFile, ZipArchiveMode.Create))
            {
                WriteEntry(zip, "ppt/slides/slide1.xml", """
                    <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                    <p:sld xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main"
                           xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
                        <p:cSld>
                            <p:spTree>
                                <p:sp>
                                    <p:txBody>
                                        <a:p><a:r><a:t>Slide Title Here</a:t></a:r></a:p>
                                        <a:p><a:r><a:t>Slide bullet point text.</a:t></a:r></a:p>
                                    </p:txBody>
                                </p:sp>
                            </p:spTree>
                        </p:cSld>
                    </p:sld>
                    """);
            }

            var text = await extractor.ExtractTextAsync(tempFile, maxFileSizeBytes: 1024 * 1024);

            Assert.IsNotNull(text);
            Assert.Contains("Slide Title Here", text);
            Assert.Contains("Slide bullet point text.", text);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [TestMethod]
    public async Task ExtractTextAsync_SyntheticPptx_ExtractsNotesAndSkipsSlideNumberFields()
    {
        var extractor = new PptxExtractor();
        var tempFile = Path.Combine(Path.GetTempPath(), $"test_pptx_{Guid.NewGuid():N}.pptx");

        try
        {
            using (var zip = ZipFile.Open(tempFile, ZipArchiveMode.Create))
            {
                WriteEntry(zip, "ppt/slides/slide1.xml", """
                    <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                    <p:sld xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main"
                           xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
                        <p:cSld>
                            <p:spTree>
                                <p:sp>
                                    <p:txBody>
                                        <a:p><a:r><a:t>Slide Title Here</a:t></a:r></a:p>
                                        <a:p><a:r><a:t>Slide bullet point text.</a:t></a:r></a:p>
                                    </p:txBody>
                                </p:sp>
                            </p:spTree>
                        </p:cSld>
                    </p:sld>
                    """);
                WriteEntry(zip, "ppt/notesSlides/notesSlide1.xml", """
                    <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                    <p:notes xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main"
                             xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
                        <p:cSld>
                            <p:spTree>
                                <p:sp>
                                    <p:txBody>
                                        <a:p><a:r><a:t>Speaker note with extra detail.</a:t></a:r></a:p>
                                        <a:p><a:fld id="{GUID}" type="slidenum"><a:t>7</a:t></a:fld></a:p>
                                    </p:txBody>
                                </p:sp>
                            </p:spTree>
                        </p:cSld>
                    </p:notes>
                    """);
            }

            var text = await extractor.ExtractTextAsync(tempFile, maxFileSizeBytes: 1024 * 1024);

            Assert.IsNotNull(text);
            Assert.Contains("Slide Title Here", text);
            Assert.Contains("Slide bullet point text.", text);
            // Notes are indexed; the repeated slide-number field is skipped.
            Assert.Contains("Speaker note with extra detail.", text);
            Assert.DoesNotContain("\n7\n", text);
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
