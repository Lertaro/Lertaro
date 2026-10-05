using System.IO.Compression;
using Lertaro.Plugins.ContentSearch.Extraction;
using Lertaro.Plugins.ContentSearch.Indexing;
using Lertaro.Plugins.ContentSearch.Storage;

namespace Lertaro.Plugins.ContentSearch.Tests.Indexing;

// Captures the process-wide PluginSdk.Logger.LogAction hook, so it must not run
// concurrently with anything that reads or resets it.
[TestClass]
[DoNotParallelize]
public sealed class IndexBatchProcessorExtractionLimitTests
{
    private string _tempDir = null!;
    private string _tempDbPath = null!;
    private ContentSearchDatabase _database = null!;
    private readonly List<string> _logLines = new();

    [TestInitialize]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "TestBatchLimit_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _tempDbPath = Path.Combine(Path.GetTempPath(), "TestBatchLimit_" + Guid.NewGuid().ToString("N") + ".db");
        _database = new ContentSearchDatabase(_tempDbPath);
        _database.Initialize();
        _logLines.Clear();
        PluginSdk.Logger.LogAction = (message, level) => _logLines.Add($"{level}: {message}");
    }

    [TestCleanup]
    public void TearDown()
    {
        PluginSdk.Logger.LogAction = null;
        _database.Dispose();
        if (File.Exists(_tempDbPath))
        {
            try { File.Delete(_tempDbPath); } catch { }
        }
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [TestMethod]
    public async Task ProcessBatchAsync_DocxWithOverLimitEntry_RecordsFailedRowWithWarning()
    {
        // A zip-bomb Word part is rejected (not indexed partially) and the failure is
        // recorded the same way a parse error is: a failed row, so the document is not
        // re-extracted on every scan, plus one warning naming the limit.
        var file = Path.Combine(_tempDir, "bomb.docx");
        WriteDocxWithOverLimitDocumentPart(file);

        var processor = new IndexBatchProcessor(_database);
        await processor.ProcessBatchAsync(new[] { file }, MakeConfig(), CancellationToken.None);

        var record = _database.GetFileRecord(file);
        Assert.IsNotNull(record);
        Assert.IsNotNull(record!.FailedAt, "an over-limit document is recorded as a failed extraction");
        Assert.IsEmpty(_database.SearchFts("aaaa", 10), "rejected content must not reach the FTS index");
        Assert.IsTrue(
            _logLines.Any(l => l.Contains("extraction limit", StringComparison.Ordinal) && l.Contains(file, StringComparison.Ordinal)),
            $"Expected a limit warning naming the file in: [{string.Join("; ", _logLines)}]");
    }

    /// <summary>
    /// One zip entry whose declared uncompressed size is past the ceiling. The data is a
    /// single repeated character, so the archive stays small even though it claims 129 MiB.
    /// </summary>
    private static void WriteDocxWithOverLimitDocumentPart(string path)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        var entry = zip.CreateEntry("word/document.xml", CompressionLevel.Fastest);
        using var stream = entry.Open();
        var chunk = new byte[1024 * 1024];
        Array.Fill(chunk, (byte)'a');

        var chunks = (ExtractionLimits.MaxEntryUncompressedBytes / chunk.Length) + 1;
        for (long i = 0; i < chunks; i++)
            stream.Write(chunk, 0, chunk.Length);
    }

    [TestMethod]
    public async Task ProcessBatchAsync_WorkbookWhoseTextPassesTheCeiling_RecordsFailedRowWithWarning()
    {
        // Every sheet entry stays under the per-entry ceiling, but their text sum does not:
        // the aggregate character ceiling has to trip mid-extraction, or the document would
        // reach SQLite as a multi-hundred-megabyte value and fail the whole batch forever.
        var file = Path.Combine(_tempDir, "huge.xlsx");
        WriteXlsxWithOverLimitText(file);

        var processor = new IndexBatchProcessor(_database);
        await processor.ProcessBatchAsync(new[] { file }, MakeConfig(), CancellationToken.None);

        var record = _database.GetFileRecord(file);
        Assert.IsNotNull(record);
        Assert.IsNotNull(record!.FailedAt, "an over-limit workbook is recorded as a failed extraction");
        Assert.IsTrue(
            _logLines.Any(l => l.Contains("character extraction limit", StringComparison.Ordinal) && l.Contains(file, StringComparison.Ordinal)),
            $"Expected a character-limit warning naming the file in: [{string.Join("; ", _logLines)}]");
    }

    /// <summary>
    /// A workbook whose sheets each hold one 2M-character cell: enough sheets to pass the
    /// aggregate character ceiling while every entry stays under the per-entry ceiling.
    /// </summary>
    private static void WriteXlsxWithOverLimitText(string path)
    {
        const int cellChars = 2 * 1024 * 1024;
        var sheetCount = (ExtractionLimits.MaxExtractedTextChars / cellChars) + 1;

        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        for (var sheet = 1; sheet <= sheetCount; sheet++)
        {
            var entry = zip.CreateEntry($"xl/worksheets/sheet{sheet}.xml", CompressionLevel.Fastest);
            using var writer = new StreamWriter(entry.Open());
            writer.Write(
                "<?xml version=\"1.0\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
                "<sheetData><row r=\"1\"><c r=\"A1\" t=\"inlineStr\"><is><t>");
            WriteRepeated(writer, 'a', cellChars);
            writer.Write("</t></is></c></row></sheetData></worksheet>");
        }
    }

    private static void WriteRepeated(TextWriter writer, char value, int count)
    {
        var chunk = new string(value, 64 * 1024);
        for (var written = 0; written < count; written += chunk.Length)
            writer.Write(chunk);
    }

    private ContentIndexConfig MakeConfig() => new()
    {
        MonitoredFolders = new List<string> { _tempDir },
        AllowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".docx", ".xlsx" },
        MaxFileSizeBytes = 64L * 1024 * 1024,
        MaxIndexSizeBytes = long.MaxValue
    };
}
