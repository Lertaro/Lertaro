using System.IO.Compression;
using System.Text;

namespace Lertaro.Plugins.ContentSearch.Extraction;

/// <summary>
/// Hard ceilings on how much decompressed data an extractor reads and how much text it
/// produces. A 10 MiB document can be a zip bomb that expands to gigabytes, and the
/// extractors run inside the App process, so an unbounded read is an app-wide out-of-memory
/// crash rather than one bad search result. Text past the SQLite insert limit would also
/// fail its whole batch on every scan. A document that passes a ceiling is rejected as a
/// failed extraction, not partially indexed: the text of a bomb is worthless, and a silently
/// truncated document would answer searches with a lie about its own contents.
/// </summary>
public static class ExtractionLimits
{
    // ponytail: deliberately hardcoded, not derived from configuration. 128 MiB is ~13x the
    // default 10 MiB per-file cap and far above what the XML parts of a real document reach
    // (a text-only Word document.xml runs a few MB per 1000 pages; the biggest realistic
    // spreadsheets' sharedStrings.xml lands in the tens of MB), while a bomb entry that
    // declares gigabytes is rejected before a single byte is decompressed. Upgrade path:
    // scale it with ContentIndexConfig.MaxFileSizeBytes if users start indexing archives
    // larger than ~100 MiB.
    public const long MaxEntryUncompressedBytes = 128L * 1024 * 1024;

    // ponytail: 32M characters (at most 128 MB as UTF-8) is the aggregate bound across all
    // parts of one document: xlsx sheets and pptx slides each stay under the entry ceiling but
    // their sum does not, and a text that size would fail the SQLite insert and poison the
    // batch on every scan. Real documents are orders of magnitude smaller (a 1000-page Word
    // document is ~5M characters). Upgrade path: raise it together with the entry ceiling,
    // keeping it well below SQLite's ~1 GB per-value limit.
    public const int MaxExtractedTextChars = 32 * 1024 * 1024;

    /// <summary>
    /// Opens a zip entry and materializes it, refusing to decompress more than
    /// <see cref="MaxEntryUncompressedBytes"/>. Reading the bytes first keeps the ceiling
    /// enforceable before an XML parser starts building a document tree from them.
    /// </summary>
    public static MemoryStream ReadEntryBounded(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        return ReadBounded(stream, entry.Length, entry.FullName);
    }

    internal static MemoryStream ReadBounded(Stream stream, long declaredLength, string sourceName)
    {
        // The declared length comes from the archive's central directory: rejecting on it
        // avoids decompressing anything at all, and the running total below catches an entry
        // whose declared length lies.
        if (declaredLength > MaxEntryUncompressedBytes)
        {
            throw new InvalidDataException(
                $"'{sourceName}' declares {declaredLength} uncompressed bytes, over the {MaxEntryUncompressedBytes}-byte extraction limit");
        }

        var memory = new MemoryStream();
        try
        {
            var buffer = new byte[64 * 1024];
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                if (memory.Length + read > MaxEntryUncompressedBytes)
                {
                    throw new InvalidDataException(
                        $"'{sourceName}' expands past the {MaxEntryUncompressedBytes}-byte extraction limit");
                }

                memory.Write(buffer, 0, read);
            }
        }
        catch
        {
            memory.Dispose();
            throw;
        }

        memory.Position = 0;
        return memory;
    }

    /// <summary>
    /// Throws once the text produced for one document passes the ceiling. Called from the
    /// append loops so the aggregate across many entries or pages stays bounded mid-extraction,
    /// not only after the whole document has already been built in memory.
    /// </summary>
    public static void ThrowIfOverTextLimit(StringBuilder builder)
    {
        if (builder.Length > MaxExtractedTextChars)
        {
            throw new InvalidDataException(
                $"extracted text exceeds the {MaxExtractedTextChars}-character extraction limit");
        }
    }
}
