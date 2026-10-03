using Lertaro.Plugins.ContentSearch.Extraction;
using Lertaro.Plugins.ContentSearch.Tests.TestSupport;

namespace Lertaro.Plugins.ContentSearch.Tests.Extraction;

// Captures the process-wide PluginSdk.Logger.LogAction hook, so it must not run
// concurrently with anything that reads or resets it.
[TestClass]
[DoNotParallelize]
public sealed class PdfExtractorLimitTests
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
    public async Task ExtractTextAsync_DocumentOverPageCeiling_GivesUpWithOneWarning()
    {
        // The page ceiling is far above real documents (3000-page reference manuals fit), so
        // it only trips on a PDF that declares more pages than any real one holds. The
        // document is refused as a whole rather than driving the page loop.
        var extractor = new PdfExtractor();
        var tempFile = Path.Combine(Path.GetTempPath(), $"test_doc_{Guid.NewGuid():N}.pdf");

        try
        {
            var pages = Enumerable.Range(0, ExtractionLimits.MaxPdfPages + 1)
                .Select(_ => "BT /F1 12 Tf 72 720 Td (page text) Tj ET")
                .ToArray();
            await File.WriteAllBytesAsync(tempFile, PdfTestDocument.Pages(pages));

            var text = await extractor.ExtractTextAsync(tempFile, maxFileSizeBytes: 64L * 1024 * 1024);

            Assert.IsNull(text);
            var giveUps = _logLines.Count(l => l.Contains("page extraction limit", StringComparison.Ordinal));
            Assert.AreEqual(1, giveUps, $"Expected exactly one ceiling warning: [{string.Join("; ", _logLines)}]");
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }
}
