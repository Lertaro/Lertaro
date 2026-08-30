using Lertaro.Plugins.ContentSearch.Indexing;
using Lertaro.Plugins.ContentSearch.Storage;
using Lertaro.Plugins.ContentSearch.Tests.TestSupport;

namespace Lertaro.Plugins.ContentSearch.Tests.Indexing;

// Captures the process-wide PluginSdk.Logger.LogAction hook, so it must not run
// concurrently with anything that reads or resets it.
[TestClass]
[DoNotParallelize]
public sealed class IndexBatchProcessorTests
{
    private string _tempDir = null!;
    private string _tempDbPath = null!;
    private ContentSearchDatabase _database = null!;
    private readonly List<string> _logLines = new();

    [TestInitialize]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "TestBatchProcessor_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _tempDbPath = Path.Combine(Path.GetTempPath(), "TestBatchProcessor_" + Guid.NewGuid().ToString("N") + ".db");
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
    public async Task ProcessBatchAsync_TextFile_IndexesContent()
    {
        var file = await WriteFileAsync("readable.txt", "indexable plain text content");
        var processor = new IndexBatchProcessor(_database);

        await processor.ProcessBatchAsync(new[] { file }, MakeConfig(), CancellationToken.None);

        var record = _database.GetFileRecord(file);
        Assert.IsNotNull(record);
        Assert.IsNull(record.FailedAt);
        Assert.HasCount(1, _database.SearchFts("indexable plain text", 10));
    }

    [TestMethod]
    public async Task ProcessBatchAsync_BinaryFileWithTextExtension_RecordsFailureWithWarning()
    {
        // OLE compound signature in a .txt: PlainTextExtractor skips it as binary,
        // and the scheduler must keep a failed row (not delete it) so the file is
        // not re-extracted on every scan.
        var file = await WriteBinaryFileAsync("diskstats.txt", [0xD0, 0xCF, 0x11, 0xE0, 0x00, 0x00, 0x00, 0x00]);
        var processor = new IndexBatchProcessor(_database);

        await processor.ProcessBatchAsync(new[] { file }, MakeConfig(), CancellationToken.None);

        var record = _database.GetFileRecord(file);
        Assert.IsNotNull(record);
        Assert.IsNotNull(record.FailedAt);
        Assert.AreEqual(new FileInfo(file).Length, record.FileSize);
        Assert.IsTrue(
            _logLines.Any(l => l.Contains("Skipped binary file", StringComparison.Ordinal) && l.Contains(file, StringComparison.Ordinal)),
            $"Expected a binary-skip warning in: [{string.Join("; ", _logLines)}]");
    }

    [TestMethod]
    public async Task ProcessBatchAsync_ImageOnlyPdf_WarnsNoExtractableText()
    {
        var file = Path.Combine(_tempDir, "scanned.pdf");
        await File.WriteAllBytesAsync(file, PdfTestDocument.SinglePage("0 0 1 rg 72 720 200 100 re f"));
        var processor = new IndexBatchProcessor(_database);

        await processor.ProcessBatchAsync(new[] { file }, MakeConfig(), CancellationToken.None);

        var record = _database.GetFileRecord(file);
        Assert.IsNotNull(record);
        Assert.IsNotNull(record.FailedAt);
        Assert.IsTrue(
            _logLines.Any(l => l.Contains("No extractable text (likely image-only PDF)", StringComparison.Ordinal) && l.Contains(file, StringComparison.Ordinal)),
            $"Expected a no-text warning in: [{string.Join("; ", _logLines)}]");
    }

    [TestMethod]
    public async Task ProcessBatchAsync_OversizedFile_KeepsFailedRowAndLogsInfo()
    {
        var file = await WriteFileAsync("huge.txt", new string('x', 2048));
        var config = MakeConfig(maxFileSizeBytes: 1024);
        var processor = new IndexBatchProcessor(_database);

        await processor.ProcessBatchAsync(new[] { file }, config, CancellationToken.None);

        var record = _database.GetFileRecord(file);
        Assert.IsNotNull(record);
        Assert.IsNotNull(record.FailedAt);
        Assert.IsFalse(
            _logLines.Any(l => l.Contains("No extractable text", StringComparison.Ordinal)),
            "An oversized skip is a policy decision, not a no-text case");
        Assert.IsTrue(
            _logLines.Any(l => l.Contains("exceeds the configured max file size", StringComparison.Ordinal)),
            $"Expected a size-skip info line in: [{string.Join("; ", _logLines)}]");
    }

    [TestMethod]
    public async Task ProcessBatchAsync_FileOutsideMonitoredFolders_DeletesRow()
    {
        var file = await WriteFileAsync("readable.txt", "indexable plain text content");
        _database.InsertOrUpdateFile(file, DateTime.UtcNow, 27, "stale content");
        var processor = new IndexBatchProcessor(_database);

        // The config points at an empty sibling folder, so the file is out of scope now.
        var otherDir = Path.Combine(_tempDir, "other");
        Directory.CreateDirectory(otherDir);
        await processor.ProcessBatchAsync(new[] { file }, MakeConfig(folder: otherDir), CancellationToken.None);

        Assert.IsNull(_database.GetFileRecord(file));
    }


    private ContentIndexConfig MakeConfig(string? folder = null, long maxFileSizeBytes = 1024 * 1024, long maxIndexSizeBytes = long.MaxValue) => new()
    {
        MonitoredFolders = new List<string> { folder ?? _tempDir },
        AllowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".txt", ".pdf" },
        MaxFileSizeBytes = maxFileSizeBytes,
        MaxIndexSizeBytes = maxIndexSizeBytes
    };

    [TestMethod]
    public async Task ProcessBatchAsync_DuplicateLargeFiles_SecondPathReusesSourceText()
    {
        // Two identical >10 MB files: whether they arrive in different batches (DB lookup)
        // or the same batch (intra-batch coordination), exactly one of them parses and
        // stores text; the other references it and the FTS index holds one copy.
        const string marker = "dedup payload marker uniquephrase";
        var payload = new string('x', (int)DuplicateContentResolver.HashThresholdBytes) + marker;
        var first = await WriteFileAsync("first.txt", payload);
        var second = await WriteFileAsync("second.txt", payload);
        var processor = new IndexBatchProcessor(_database);
        var config = MakeConfig(maxFileSizeBytes: 512L * 1024 * 1024);

        // Different batches: the second file's DB lookup must hit the first's hash.
        await processor.ProcessBatchAsync(new[] { first }, config, CancellationToken.None);
        await processor.ProcessBatchAsync(new[] { second }, config, CancellationToken.None);

        var firstRecord = _database.GetFileRecord(first)!;
        var secondRecord = _database.GetFileRecord(second)!;
        Assert.IsNull(firstRecord.ContentRef);
        Assert.IsNotNull(secondRecord.ContentRef);
        Assert.AreEqual(firstRecord.Id, secondRecord.ContentRef);
        Assert.IsNull(secondRecord.FailedAt, "a duplicate is indexed, not failed");

        var hits = _database.SearchFts("dedup payload marker", 10);
        Assert.HasCount(2, hits);
        Assert.IsTrue(hits.All(h => h.Snippet.Contains("dedup payload marker", StringComparison.Ordinal)),
            "the duplicate must surface the source row's text in its snippet");

        // Same batch, source still in the DB: both files resolve to the existing source
        // directly, no intra-batch coordination needed.
        var third = await WriteFileAsync("third.txt", payload);
        var fourth = await WriteFileAsync("fourth.txt", payload);
        await processor.ProcessBatchAsync(new[] { third, fourth }, config, CancellationToken.None);

        Assert.AreEqual(firstRecord.Id, _database.GetFileRecord(third)!.ContentRef);
        Assert.AreEqual(firstRecord.Id, _database.GetFileRecord(fourth)!.ContentRef);

        // Same batch, no source in the DB (deleted): both DB lookups miss, so the
        // intra-batch coordination must pick one as source and reference it from the other.
        _database.DeleteFilesBatch(new[] { first });
        var fifth = await WriteFileAsync("fifth.txt", payload);
        var sixth = await WriteFileAsync("sixth.txt", payload);
        await processor.ProcessBatchAsync(new[] { fifth, sixth }, config, CancellationToken.None);

        var fifthRecord = _database.GetFileRecord(fifth)!;
        var sixthRecord = _database.GetFileRecord(sixth)!;
        // Which one becomes the source is unspecified (the batch is processed in parallel);
        // exactly one holds the text, the other references it.
        var fifthIsSource = fifthRecord.ContentRef == null && sixthRecord.ContentRef == fifthRecord.Id;
        var sixthIsSource = sixthRecord.ContentRef == null && fifthRecord.ContentRef == sixthRecord.Id;
        Assert.IsTrue(fifthIsSource || sixthIsSource,
            $"expected one source and one duplicate, got refs {fifthRecord.ContentRef}/{sixthRecord.ContentRef}");
        Assert.IsNull(fifthRecord.FailedAt);
        Assert.IsNull(sixthRecord.FailedAt);
    }

    [TestMethod]
    public async Task ProcessBatchAsync_IndexOverSizeCap_SkipsWholeBatchWithWarning()
    {
        var file = await WriteFileAsync("readable.txt", "indexable plain text content");
        var config = MakeConfig(maxIndexSizeBytes: 1); // 1 byte cap: always over
        var processor = new IndexBatchProcessor(_database);

        await processor.ProcessBatchAsync(new[] { file }, config, CancellationToken.None);

        Assert.IsNull(_database.GetFileRecord(file), "an over-budget batch must not write anything");
        Assert.IsTrue(
            _logLines.Any(l => l.Contains("Index size cap reached", StringComparison.Ordinal)),
            $"Expected a cap warning in: [{string.Join("; ", _logLines)}]");
    }

    private async Task<string> WriteFileAsync(string name, string content)
    {
        var path = Path.Combine(_tempDir, name);
        await File.WriteAllTextAsync(path, content);
        return path;
    }

    private async Task<string> WriteBinaryFileAsync(string name, byte[] content)
    {
        var path = Path.Combine(_tempDir, name);
        await File.WriteAllBytesAsync(path, content);
        return path;
    }
}
