using System.Text;

namespace Lertaro.Plugins.ContentSearch.Tests.TestSupport;

/// <summary>
/// Builds minimal in-memory PDF bytes for extractor tests so no fixture files are needed.
/// Each page is given as a raw content stream; pages without any text-showing operator
/// (Tj/TJ) act as image-only pages for the "no extractable text" cases.
/// </summary>
internal static class PdfTestDocument
{
    public static byte[] TwoPage(string firstPageContentStream, string secondPageContentStream) =>
        Build(firstPageContentStream, secondPageContentStream);

    public static byte[] SinglePage(string contentStream) => Build(contentStream);

    public static byte[] Pages(params string[] pageContentStreams) => Build(pageContentStreams);

    private static byte[] Build(params string[] pageStreams)
    {
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [" + string.Join(" ", pageStreams.Select((_, i) => $"{i + 3} 0 R")) + $"] /Count {pageStreams.Length} >>",
        };

        for (var i = 0; i < pageStreams.Length; i++)
        {
            objects.Add(
                $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 {pageStreams.Length + 3} 0 R >> >> /Contents {pageStreams.Length + 4 + i} 0 R >>");
        }

        objects.Add("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>");
        objects.AddRange(pageStreams.Select(
            stream => $"<< /Length {stream.Length} >>\nstream\n{stream}\nendstream"));

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
