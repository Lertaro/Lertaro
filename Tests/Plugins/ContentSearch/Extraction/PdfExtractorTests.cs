using Lertaro.Plugins.ContentSearch.Extraction;
using Lertaro.Plugins.ContentSearch.Tests.TestSupport;

namespace Lertaro.Plugins.ContentSearch.Tests.Extraction;

// Captures the process-wide PluginSdk.Logger.LogAction hook, so it must not run
// concurrently with anything that reads or resets it.
[TestClass]
[DoNotParallelize]
public sealed class PdfExtractorTests
{
    [TestMethod]
    public void CanHandle_PdfExtension_Only()
    {
        var extractor = new PdfExtractor();
        Assert.IsTrue(extractor.CanHandle(".pdf"));
        Assert.IsFalse(extractor.CanHandle(".txt"));
    }

    [TestMethod]
    public async Task ExtractTextAsync_PlainTwoPageDocument_ReturnsAllPages()
    {
        var extractor = new PdfExtractor();
        var tempFile = Path.Combine(Path.GetTempPath(), $"test_doc_{Guid.NewGuid():N}.pdf");

        try
        {
            await File.WriteAllBytesAsync(tempFile, PdfTestDocument.TwoPage(TextStream("first page words"), TextStream("second page words")));
            var text = await extractor.ExtractTextAsync(tempFile, maxFileSizeBytes: 1024 * 1024);

            Assert.IsNotNull(text);
            Assert.Contains("first page words", text);
            Assert.Contains("second page words", text);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [TestMethod]
    public async Task ExtractTextAsync_PageWithSlightlyRotatedGlyphs_ExtractsAllPages()
    {
        // PdfPig 0.1.9 threw "Could not find TextOrientation for rotation" for glyphs with zero
        // advance width on a ~4 degree rotated text matrix, which forced the extractor to skip
        // such pages (real case: a PDF whose page 4 has such glyphs lost that page entirely).
        // Fixed upstream in 0.1.10; this asserts the rotated page is now fully extracted.
        var extractor = new PdfExtractor();
        var tempFile = Path.Combine(Path.GetTempPath(), $"test_doc_{Guid.NewGuid():N}.pdf");

        try
        {
            // Zero horizontal scale collapses glyph advances; the precise 4 degree matrix used
            // to reach Letter.GetTextOrientationRot with an unresolvable rotation in 0.1.9.
            const string rotatedPage =
                "BT 0.99756405 0.06975647 -0.06975647 0.99756405 72 720 Tm /F1 12 Tf 0 Tz (Rotated page) Tj ET";
            await File.WriteAllBytesAsync(tempFile, PdfTestDocument.TwoPage(TextStream("Cysteine normal page"), rotatedPage));

            var text = await extractor.ExtractTextAsync(tempFile, maxFileSizeBytes: 1024 * 1024);

            Assert.IsNotNull(text);
            Assert.Contains("Cysteine normal page", text);
            Assert.Contains("Rotated page", text);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [TestMethod]
    public async Task ExtractTextAsync_ImageOnlyPdf_ReturnsEmptyStringNotFailure()
    {
        // A well-formed PDF whose pages draw only shapes (no text operators) must come back
        // as an empty string, not null: null means "extraction failed" to the scheduler,
        // while empty means "no text layer" and earns its own distinct warning.
        var extractor = new PdfExtractor();
        var tempFile = Path.Combine(Path.GetTempPath(), $"test_doc_{Guid.NewGuid():N}.pdf");

        try
        {
            // A filled rectangle and a stroked line: real visible content, zero text.
            const string graphicsOnly = "0 0 1 rg 72 720 200 100 re f  0 0 0 RG 72 400 m 300 400 l S";
            await File.WriteAllBytesAsync(tempFile, PdfTestDocument.SinglePage(graphicsOnly));

            var text = await extractor.ExtractTextAsync(tempFile, maxFileSizeBytes: 1024 * 1024);

            Assert.IsNotNull(text);
            Assert.IsEmpty(text.Trim());
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [TestMethod]
    public async Task ExtractTextAsync_TruncatedXrefTable_FailsWithNull()
    {
        // A file whose xref/startxref trailer is missing must surface as a null (hard
        // failure, the extractor logs it), never as an empty-string "no text" result.
        var extractor = new PdfExtractor();
        var tempFile = Path.Combine(Path.GetTempPath(), $"test_doc_{Guid.NewGuid():N}.pdf");

        try
        {
            var valid = PdfTestDocument.SinglePage(TextStream("readable words"));
            // Chop the file right after the last object: no xref, no startxref, no trailer.
            var truncated = valid.AsSpan(0, valid.Length - 60).ToArray();
            await File.WriteAllBytesAsync(tempFile, truncated);

            var text = await extractor.ExtractTextAsync(tempFile, maxFileSizeBytes: 1024 * 1024);

            Assert.IsNull(text);
            Assert.IsTrue(
                _logLines.Any(l => l.Contains("Failed to extract PDF", StringComparison.Ordinal) && l.Contains(tempFile, StringComparison.Ordinal)),
                $"Expected a failure warning in: [{string.Join("; ", _logLines)}]");
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    private readonly List<string> _logLines = new();

    [TestInitialize]
    public void CaptureLogs()
    {
        _logLines.Clear();
        PluginSdk.Logger.LogAction = (message, level) => _logLines.Add($"{level}: {message}");
    }

    [TestCleanup]
    public void ReleaseLogs() => PluginSdk.Logger.LogAction = null;

    private static string TextStream(string text) => $"BT /F1 12 Tf 72 720 Td ({text}) Tj ET";
}
