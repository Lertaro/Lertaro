using Lertaro.Plugins.ContentSearch.Extraction;

namespace Lertaro.Plugins.ContentSearch.Tests.Extraction;

[TestClass]
public sealed class PlainTextExtractorTests
{
    [TestMethod]
    public void CanHandle_SupportedExtensions_ReturnsTrue()
    {
        var extractor = new PlainTextExtractor();
        Assert.IsTrue(extractor.CanHandle(".txt"));
        Assert.IsTrue(extractor.CanHandle(".md"));
        Assert.IsTrue(extractor.CanHandle(".cs"));
        Assert.IsTrue(extractor.CanHandle(".json"));
        Assert.IsFalse(extractor.CanHandle(".pdf"));
        Assert.IsFalse(extractor.CanHandle(".docx"));
    }

    [TestMethod]
    public async Task ExtractTextAsync_ValidTextFile_ExtractsContent()
    {
        var extractor = new PlainTextExtractor();
        var tempFile = Path.Combine(Path.GetTempPath(), $"test_doc_{Guid.NewGuid():N}.txt");

        try
        {
            await File.WriteAllTextAsync(tempFile, "Sample plain text extraction testing.");
            var text = await extractor.ExtractTextAsync(tempFile, maxFileSizeBytes: 1024 * 1024);

            Assert.IsNotNull(text);
            Assert.AreEqual("Sample plain text extraction testing.", text.Trim());
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [TestMethod]
    public async Task ExtractTextAsync_OversizedFile_ReturnsNull()
    {
        var extractor = new PlainTextExtractor();
        var tempFile = Path.Combine(Path.GetTempPath(), $"test_doc_{Guid.NewGuid():N}.txt");

        try
        {
            await File.WriteAllTextAsync(tempFile, "This is too big for the limit.");
            var text = await extractor.ExtractTextAsync(tempFile, maxFileSizeBytes: 5); // 5 bytes limit

            Assert.IsNull(text);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }
}
