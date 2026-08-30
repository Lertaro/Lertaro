using System.Text;
using Lertaro.Plugins.ContentSearch.Extraction;

namespace Lertaro.Plugins.ContentSearch.Tests.Extraction;

[TestClass]
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
            await File.WriteAllBytesAsync(tempFile, BuildTwoPagePdf(TextStream("first page words"), TextStream("second page words")));
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
            await File.WriteAllBytesAsync(tempFile, BuildTwoPagePdf(TextStream("Cysteine normal page"), rotatedPage));

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

    private static string TextStream(string text) => $"BT /F1 12 Tf 72 720 Td ({text}) Tj ET";

    private static byte[] BuildTwoPagePdf(string firstPageContentStream, string secondPageContentStream)
    {
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R 4 0 R] /Count 2 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 5 0 R >> >> /Contents 6 0 R >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 5 0 R >> >> /Contents 7 0 R >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
        };

        objects.Add($"<< /Length {firstPageContentStream.Length} >>\nstream\n{firstPageContentStream}\nendstream");
        objects.Add($"<< /Length {secondPageContentStream.Length} >>\nstream\n{secondPageContentStream}\nendstream");

        var sb = new StringBuilder();
        sb.Append("%PDF-1.4\n");
        var offsets = new List<int>();
        foreach (var (obj, idx) in objects.Select((o, i) => (o, i)))
        {
            offsets.Add(sb.Length);
            sb.Append($"{idx + 1} 0 obj\n{obj}\nendobj\n");
        }

        var xrefStart = sb.Length;
        sb.Append($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets)
            sb.Append($"{offset:D10} 00000 n \n");
        sb.Append($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xrefStart}\n%%EOF\n");

        return Encoding.Latin1.GetBytes(sb.ToString());
    }
}
