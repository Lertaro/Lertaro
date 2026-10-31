using Lertaro.Plugins.ContentSearch.Extraction;

namespace Lertaro.Plugins.ContentSearch.Tests.Extraction;

// [DoNotParallelize]: the process-wide PluginSdk.Logger.LogAction hook -- see IndexBatchProcessorTests' header.
[TestClass]
[DoNotParallelize]
public sealed class PlainTextExtractorTests
{
    private readonly List<string> _logLines = new();

    [TestInitialize]
    public void CaptureLogs()
    {
        _logLines.Clear();
        PluginSdk.Logger.LogAction = (message, level) => _logLines.Add($"{level}: {message}");
    }

    [TestCleanup]
    public void ReleaseLogs() => PluginSdk.Logger.LogAction = null;

    [TestMethod]
    public void CanHandle_AnyNonEmptyExtension_ReturnsTrue()
    {
        var extractor = new PlainTextExtractor();
        Assert.IsTrue(extractor.CanHandle(".txt"));
        Assert.IsTrue(extractor.CanHandle(".md"));
        Assert.IsTrue(extractor.CanHandle(".cs"));
        Assert.IsTrue(extractor.CanHandle(".vue"));
        Assert.IsTrue(extractor.CanHandle(".custom"));
        Assert.IsFalse(extractor.CanHandle(""));
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

    [TestMethod]
    public async Task ExtractTextAsync_GbkEncodedFile_ExtractsReadableChinese()
    {
        var extractor = new PlainTextExtractor();
        var tempFile = Path.Combine(Path.GetTempPath(), $"test_doc_{Guid.NewGuid():N}.txt");

        try
        {
            // "这是文本文档" in GBK plus a CRLF, mirroring a legacy Windows notepad file.
            var gbkBytes = new byte[]
            {
                0xD5, 0xE2, 0xCA, 0xC7, 0xCE, 0xC4, 0xB1, 0xBE, 0xCE, 0xC4, 0xB5, 0xB5, 0x0D, 0x0A
            };
            await File.WriteAllBytesAsync(tempFile, gbkBytes);

            var text = await extractor.ExtractTextAsync(tempFile, maxFileSizeBytes: 1024 * 1024);

            Assert.IsNotNull(text);
            Assert.AreEqual("这是文本文档", text.Trim());
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [TestMethod]
    public async Task ExtractTextAsync_BinaryFileWithoutExtractor_SkipsAndLogsWarning()
    {
        var extractor = new PlainTextExtractor();
        var tempFile = Path.Combine(Path.GetTempPath(), $"test_doc_{Guid.NewGuid():N}.doc");

        try
        {
            // OLE compound file signature bytes as written by legacy .doc/.xls/.ppt, followed
            // by the NUL-padded directory sector: typing "doc" into the whitelist lands here.
            var oleBytes = new byte[512];
            oleBytes[0] = 0xD0; oleBytes[1] = 0xCF; oleBytes[2] = 0x11; oleBytes[3] = 0xE0;
            oleBytes[4] = 0xA1; oleBytes[5] = 0xB1; oleBytes[6] = 0x1A; oleBytes[7] = 0xE1;
            await File.WriteAllBytesAsync(tempFile, oleBytes);

            var text = await extractor.ExtractTextAsync(tempFile, maxFileSizeBytes: 1024 * 1024);

            Assert.IsNull(text);
            Assert.IsTrue(
                _logLines.Any(l => l.Contains("Skipped binary file", StringComparison.Ordinal) && l.Contains(tempFile, StringComparison.Ordinal)),
                $"Expected a skip warning in: [{string.Join("; ", _logLines)}]");
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }
}
