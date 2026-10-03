using Lertaro.Plugins.ContentSearch.Indexing;
using Lertaro.Plugins.ContentSearch.Storage;

namespace Lertaro.Plugins.ContentSearch.Tests.Indexing;

// Captures the process-wide PluginSdk.Logger.LogAction hook, so it must not run
// concurrently with anything that reads or resets it.
[TestClass]
[DoNotParallelize]
public sealed class IndexBatchProcessorMissingFileTests
{
    private string _tempDir = null!;
    private string _tempDbPath = null!;
    private ContentSearchDatabase _database = null!;
    private readonly List<string> _logLines = new();

    [TestInitialize]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "TestBatchMissing_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _tempDbPath = Path.Combine(Path.GetTempPath(), "TestBatchMissing_" + Guid.NewGuid().ToString("N") + ".db");
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
    public async Task ProcessBatchAsync_FileMomentarilyInvisible_KeepsRowAndItsText()
    {
        // Regression: File.Exists == false used to delete the row and its FTS text on the
        // spot, so one stalled SMB listing lost indexed content permanently. The row now
        // takes the same grace a file missing at scan time takes.
        var file = await WriteAndIndexAsync("vanished.txt");
        File.Delete(file);

        var processor = new IndexBatchProcessor(_database);
        await processor.ProcessBatchAsync(new[] { file }, MakeConfig(), CancellationToken.None);

        var record = _database.GetFileRecord(file);
        Assert.IsNotNull(record, "a momentary miss must not delete the row");
        Assert.AreEqual(1, record!.MissingCount);
        Assert.HasCount(1, _database.SearchFts("indexed text", 10));
    }

    [TestMethod]
    public async Task ProcessBatchAsync_FileInvisibleAtRetryLimit_DeletesRowAndItsText()
    {
        // The grace is not a reprieve: once the retry limit is reached the row goes, exactly
        // as the scan-time retention pass removes a genuinely gone file.
        var file = await WriteAndIndexAsync("gone.txt");
        File.Delete(file);
        _database.UpdateMissingCounts(new Dictionary<string, int> { [file] = 2 });

        var processor = new IndexBatchProcessor(_database);
        await processor.ProcessBatchAsync(new[] { file }, MakeConfig(), CancellationToken.None);

        Assert.IsNull(_database.GetFileRecord(file));
        Assert.IsEmpty(_database.SearchFts("indexed text", 10));
    }

    [TestMethod]
    public async Task ProcessBatchAsync_UnreadableLargeFile_DoesNotAbortRestOfBatch()
    {
        // The dedup hash gives up on an unreadable large file (here: an exclusive lock) and
        // the lane records a failed row instead of letting the failure escape Task.WhenAll:
        // the rest of the batch still indexes.
        var locked = Path.Combine(_tempDir, "locked.txt");
        await File.WriteAllBytesAsync(locked, new byte[DuplicateContentResolver.HashThresholdBytes + 1]);
        var good = await WriteAndIndexAsync("good.txt", insertRow: false);

        var processor = new IndexBatchProcessor(_database);
        using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            await processor.ProcessBatchAsync(new[] { locked, good }, MakeConfig(maxFileSizeBytes: 64L * 1024 * 1024), CancellationToken.None);
        }

        var goodRecord = _database.GetFileRecord(good);
        Assert.IsNotNull(goodRecord);
        Assert.IsNull(goodRecord!.FailedAt, "the healthy file of the same batch must still be indexed");
        Assert.HasCount(1, _database.SearchFts("indexed text", 10));

        var lockedRecord = _database.GetFileRecord(locked);
        Assert.IsNotNull(lockedRecord, "the unreadable file keeps a failed row so it is not retried every scan");
        Assert.IsNotNull(lockedRecord!.FailedAt);
        Assert.IsTrue(
            _logLines.Any(l => l.Contains("Could not hash", StringComparison.Ordinal) && l.Contains(locked, StringComparison.Ordinal)),
            $"Expected a hash give-up warning in: [{string.Join("; ", _logLines)}]");
    }

    private async Task<string> WriteAndIndexAsync(string name, bool insertRow = true)
    {
        var path = Path.Combine(_tempDir, name);
        await File.WriteAllTextAsync(path, "indexed text");
        if (insertRow)
            _database.InsertOrUpdateFile(path, DateTime.UtcNow.AddMinutes(-5), new FileInfo(path).Length, "indexed text");
        return path;
    }

    private ContentIndexConfig MakeConfig(long maxFileSizeBytes = 1024 * 1024) => new()
    {
        MonitoredFolders = new List<string> { _tempDir },
        AllowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".txt" },
        MaxFileSizeBytes = maxFileSizeBytes,
        MaxIndexSizeBytes = long.MaxValue
    };
}
