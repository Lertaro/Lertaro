using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace Lertaro.Plugins.ContentSearch.Extraction;

/// <summary>
/// Extracts searchable text from Word (.docx/.docm) documents using built-in ZipArchive and XML parsing.
/// Covers the body plus footnotes, endnotes, comments, headers, and footers, mirroring the parts
/// dnGrep's WordReader extracts; those parts live in separate zip entries that a body-only read misses.
/// </summary>
public sealed class DocxExtractor : ITextExtractor
{
    private const int MaxExtractedCharacters = 500_000;
    private static readonly XNamespace WordNs = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    public bool CanHandle(string extension) =>
        string.Equals(extension, ".docx", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(extension, ".docm", StringComparison.OrdinalIgnoreCase);

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
                return ExtractDocumentText(archive, timeoutCts.Token);
            }, timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            PluginSdk.Logger.Log(
                $"[ContentSearch] Timed out extracting Word document '{filePath}'",
                PluginSdk.LogLevel.Warn);
            return null;
        }
        catch (Exception ex)
        {
            PluginSdk.Logger.Log(
                $"[ContentSearch] Failed to extract Word document '{filePath}': {ex.Message}",
                PluginSdk.LogLevel.Warn);
            return null;
        }
    }

    private static string? ExtractDocumentText(ZipArchive archive, CancellationToken ct)
    {
        var builder = new StringBuilder();

        AppendParagraphsFromEntry(archive.GetEntry("word/document.xml"), builder, ct);
        AppendNoteParts(archive.GetEntry("word/footnotes.xml"), "footnote", builder, ct);
        AppendNoteParts(archive.GetEntry("word/endnotes.xml"), "endnote", builder, ct);
        AppendCommentParts(archive.GetEntry("word/comments.xml"), builder, ct);
        AppendPrefixedParts(archive, "word/header", builder, ct);
        AppendPrefixedParts(archive, "word/footer", builder, ct);

        // Empty string (not null) for a well-formed but textless document; null is
        // reserved for actual extraction failures.
        return builder.ToString();
    }

    private static void AppendParagraphsFromEntry(ZipArchiveEntry? entry, StringBuilder builder, CancellationToken ct)
    {
        if (entry == null) return;

        using var stream = entry.Open();
        var xDoc = XDocument.Load(stream);
        if (xDoc.Root == null) return;

        foreach (var paragraph in xDoc.Descendants(WordNs + "p"))
        {
            ct.ThrowIfCancellationRequested();
            AppendParagraphText(paragraph, builder);
            if (builder.Length >= MaxExtractedCharacters)
                return;
        }
    }

    private static void AppendNoteParts(ZipArchiveEntry? entry, string noteElementName, StringBuilder builder, CancellationToken ct)
    {
        if (entry == null) return;

        using var stream = entry.Open();
        var xDoc = XDocument.Load(stream);
        if (xDoc.Root == null) return;

        // Notes with ids 0 and -1 hold the separator and continuation marks, not real content.
        foreach (var note in xDoc.Descendants(WordNs + noteElementName))
        {
            if (!long.TryParse(note.Attribute(WordNs + "id")?.Value, out var id) || id <= 0)
                continue;

            foreach (var paragraph in note.Descendants(WordNs + "p"))
            {
                ct.ThrowIfCancellationRequested();
                AppendParagraphText(paragraph, builder);
                if (builder.Length >= MaxExtractedCharacters)
                    return;
            }
        }
    }

    private static void AppendCommentParts(ZipArchiveEntry? entry, StringBuilder builder, CancellationToken ct)
    {
        if (entry == null) return;

        using var stream = entry.Open();
        var xDoc = XDocument.Load(stream);
        if (xDoc.Root == null) return;

        foreach (var comment in xDoc.Descendants(WordNs + "comment"))
        {
            ct.ThrowIfCancellationRequested();
            var author = comment.Attribute(WordNs + "author")?.Value;
            if (!string.IsNullOrWhiteSpace(author))
                builder.AppendLine(author.Trim());

            foreach (var paragraph in comment.Descendants(WordNs + "p"))
            {
                AppendParagraphText(paragraph, builder);
            }

            if (builder.Length >= MaxExtractedCharacters)
                return;
        }
    }

    private static void AppendPrefixedParts(ZipArchive archive, string entryNamePrefix, StringBuilder builder, CancellationToken ct)
    {
        var entries = archive.Entries
            .Where(e => e.FullName.StartsWith(entryNamePrefix, StringComparison.OrdinalIgnoreCase) &&
                        e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase));

        foreach (var entry in entries)
        {
            AppendParagraphsFromEntry(entry, builder, ct);
            if (builder.Length >= MaxExtractedCharacters)
                return;
        }
    }

    private static void AppendParagraphText(XElement paragraph, StringBuilder builder)
    {
        var text = new StringBuilder();
        foreach (var node in paragraph.Descendants())
        {
            if (node.Name == WordNs + "t")
                text.Append(node.Value);
            else if (node.Name == WordNs + "tab")
                text.Append('\t');
            else if (node.Name == WordNs + "br")
                text.Append(' ');
            else if (node.Name == WordNs + "noBreakHyphen")
                text.Append('\u2011');
        }

        if (text.Length > 0)
            builder.AppendLine(text.ToString());
    }
}
