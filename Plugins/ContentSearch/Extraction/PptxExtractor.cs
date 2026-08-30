using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace Lertaro.Plugins.ContentSearch.Extraction;

/// <summary>
/// Extracts searchable slide text from PowerPoint (.pptx) presentations using built-in ZipArchive and
/// DrawingML parsing. Covers speaker notes slides in addition to the slides themselves, mirroring
/// dnGrep's PowerPointReader; notes live in separate zip entries that a slides-only read misses.
/// </summary>
public sealed class PptxExtractor : ITextExtractor
{
    private const int MaxExtractedCharacters = 500_000;
    private static readonly XNamespace DrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";

    public bool CanHandle(string extension) =>
        string.Equals(extension, ".pptx", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(extension, ".pptm", StringComparison.OrdinalIgnoreCase);

    public async Task<string?> ExtractTextAsync(string filePath, long maxFileSizeBytes, CancellationToken cancellationToken = default)
    {
        var fileInfo = new FileInfo(filePath);
        if (!fileInfo.Exists || fileInfo.Length > maxFileSizeBytes || fileInfo.Length == 0)
            return null;

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(ExtractorTimeoutPolicy.ForFileSize(fileInfo.Length));

        try
        {
            return await Task.Run(() =>
            {
                timeoutCts.Token.ThrowIfCancellationRequested();
                using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var archive = new ZipArchive(fileStream, ZipArchiveMode.Read, leaveOpen: false);
                return ExtractPresentationText(archive, timeoutCts.Token);
            }, timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            PluginSdk.Logger.Log(
                $"[ContentSearch] Timed out extracting presentation '{filePath}'",
                PluginSdk.LogLevel.Warn);
            return null;
        }
        catch (Exception ex)
        {
            PluginSdk.Logger.Log(
                $"[ContentSearch] Failed to extract presentation '{filePath}': {ex.Message}",
                PluginSdk.LogLevel.Warn);
            return null;
        }
    }

    private static string? ExtractPresentationText(ZipArchive archive, CancellationToken ct)
    {
        var builder = new StringBuilder();

        foreach (var entry in SlidesOrNotes(archive, "ppt/slides/slide"))
        {
            ct.ThrowIfCancellationRequested();
            AppendSlideText(entry, builder, ct, skipSlideNumberFields: false);
            if (builder.Length >= MaxExtractedCharacters)
                return builder.ToString();
        }

        foreach (var entry in SlidesOrNotes(archive, "ppt/notesSlides/notesSlide"))
        {
            ct.ThrowIfCancellationRequested();
            AppendSlideText(entry, builder, ct, skipSlideNumberFields: true);
            if (builder.Length >= MaxExtractedCharacters)
                return builder.ToString();
        }

        return builder.Length > 0 ? builder.ToString() : null;
    }

    private static IEnumerable<ZipArchiveEntry> SlidesOrNotes(ZipArchive archive, string entryNamePrefix) =>
        archive.Entries
            .Where(e => e.FullName.StartsWith(entryNamePrefix, StringComparison.OrdinalIgnoreCase) &&
                        e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => e.FullName, StringComparer.OrdinalIgnoreCase);

    private static void AppendSlideText(ZipArchiveEntry entry, StringBuilder builder, CancellationToken ct, bool skipSlideNumberFields)
    {
        using var slideStream = entry.Open();
        var xDoc = XDocument.Load(slideStream);
        if (xDoc.Root == null) return;

        foreach (var paragraph in xDoc.Descendants(DrawingNs + "p"))
        {
            ct.ThrowIfCancellationRequested();
            var text = new StringBuilder();
            foreach (var node in paragraph.Descendants())
            {
                if (node.Name == DrawingNs + "t")
                {
                    // Notes slides repeat the slide number field on every page; indexing it
                    // would pollute the full-text index with meaningless page counters.
                    if (skipSlideNumberFields && IsSlideNumberField(node))
                        continue;
                    text.Append(node.Value);
                }
                else if (node.Name == DrawingNs + "br")
                {
                    text.Append(' ');
                }
            }

            if (text.Length > 0)
                builder.AppendLine(text.ToString());

            if (builder.Length >= MaxExtractedCharacters)
                return;
        }
    }

    private static bool IsSlideNumberField(XElement textElement) =>
        textElement.Ancestors(DrawingNs + "fld")
            .Any(fld => (string?)fld.Attribute("type") == "slidenum");
}
