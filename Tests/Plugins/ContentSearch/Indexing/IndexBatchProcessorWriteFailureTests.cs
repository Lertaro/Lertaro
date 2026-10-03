using Lertaro.Plugins.ContentSearch.Indexing;
using Lertaro.Plugins.ContentSearch.Storage;
using Microsoft.Data.Sqlite;

namespace Lertaro.Plugins.ContentSearch.Tests.Indexing;

// End-to-end for write-phase isolation: one document the database refuses must not wedge its
// whole batch. The refusal here is a trigger rejecting the insert of that file's own row -- the
// shape a SQLITE_TOOBIG on a pathologically large document takes, where the batch's single
// transaction rolls back on that one item. The trigger lets the failed row (failed_at set)
// through, which is exactly what the recovery needs.
// Captures the process-wide PluginSdk.Logger.LogAction hook, so it must not run concurrently with
// anything that reads or resets it.
[TestClass]
[DoNotParallelize]
public sealed class IndexBatchProcessorWriteFailureTests
{
    private string _tempDir = null!;
    private string _tempDbPath = null!;
    private ContentSearchDatabase _database = null!;
    private readonly List<string> _logLines = new();

    [TestInitialize]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "TestBatchWriteFail_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _tempDbPath = Path.Combine(Path.GetTempPath(), "TestBatchWriteFail_" + Guid.NewGuid().ToString("N") + ".db");
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
    public async Task ProcessBatchAsync_OneUnwritableDocument_HealthyFilesLandAndItIsRecordedAsFailed()
    {
        var poison = await WriteFileAsync("too-big.txt", "quarterly 报告单 poison payload");
        var healthyA = await WriteFileAsync("healthy-a.txt", "alpha plain text body");
        var healthyB = await WriteFileAsync("healthy-b.txt", "bravo plain text body");
        RefuseTheRowOf(poison);

        var processor = new IndexBatchProcessor(_database);
        await processor.ProcessBatchAsync(new[] { poison, healthyA, healthyB }, MakeConfig(), CancellationToken.None);

        // The healthy files of the poisoned batch still landed.
        foreach (var healthy in new[] { healthyA, healthyB })
        {
            var record = _database.GetFileRecord(healthy);
            Assert.IsNotNull(record, $"'{healthy}' shares a batch with the unwritable file and must still be indexed");
            Assert.IsNull(record.FailedAt);
        }

        Assert.HasCount(1, _database.SearchFts("alpha plain text", 10));
        Assert.HasCount(1, _database.SearchFts("bravo plain text", 10));

        // The offender is a failed row instead of a batch-wide loss, and its text never reached the index.
        var poisonRecord = _database.GetFileRecord(poison);
        Assert.IsNotNull(poisonRecord, "the unwritable file is recorded, not silently dropped");
        Assert.IsNotNull(poisonRecord.FailedAt, "a document the database refuses is a failed extraction");
        Assert.IsEmpty(_database.SearchFts("报告单 poison payload", 10));
        Assert.IsTrue(
            _logLines.Any(l => l.Contains(poison, StringComparison.Ordinal)),
            $"Expected a warning naming the unwritable file in: [{string.Join("; ", _logLines)}]");
    }

    [TestMethod]
    public async Task ProcessBatchAsync_RecordedWriteFailure_CarriesTheMetadataDiscoveryCompares()
    {
        // A failed row only stops the retry loop because discovery compares the row's mtime/size
        // with the file and finds them equal: recording the failure with stale metadata would
        // re-enqueue and re-fail the file on every scan, which is the wedge this replaces.
        var poison = await WriteFileAsync("too-big.txt", "quarterly poison payload");
        RefuseTheRowOf(poison);
        var info = new FileInfo(poison);

        var processor = new IndexBatchProcessor(_database);
        await processor.ProcessBatchAsync(new[] { poison }, MakeConfig(), CancellationToken.None);

        var meta = _database.GetAllFileMetadata();
        Assert.IsTrue(meta.ContainsKey(poison), "a failed row must exist for discovery to compare against");
        Assert.AreEqual(info.Length, meta[poison].FileSize);
        Assert.AreEqual(new DateTimeOffset(info.LastWriteTimeUtc).ToUnixTimeSeconds(), meta[poison].LastModified);
    }

    /// <summary>
    /// Makes one INSERT INTO files fail while leaving everything else the table does alone: the
    /// failing statement is the file's own row, so the batch write's transaction rolls back on it.
    /// The WHEN clause exempts rows with failed_at set, so the failed row written in recovery is
    /// accepted -- without that the test could not tell recovery apart from a wedged batch.
    /// </summary>
    private void RefuseTheRowOf(string path)
    {
        using var conn = new SqliteConnection($"Data Source={_tempDbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        var quotedPath = path.Replace("'", "''", StringComparison.Ordinal);
        cmd.CommandText = $"""
            CREATE TRIGGER test_refuse_document BEFORE INSERT ON files
            WHEN NEW.path = '{quotedPath}' AND NEW.failed_at IS NULL
            BEGIN SELECT RAISE(ABORT, 'document refused'); END;
            """;
        cmd.ExecuteNonQuery();
    }

    private ContentIndexConfig MakeConfig() => new()
    {
        MonitoredFolders = new List<string> { _tempDir },
        AllowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".txt" },
        MaxFileSizeBytes = 1024 * 1024,
        MaxIndexSizeBytes = long.MaxValue
    };

    private async Task<string> WriteFileAsync(string name, string content)
    {
        var path = Path.Combine(_tempDir, name);
        await File.WriteAllTextAsync(path, content);
        return path;
    }
}
