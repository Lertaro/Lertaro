using Lertaro.Plugins.ContentSearch.Indexing;
using Lertaro.Plugins.ContentSearch.Storage;

namespace Lertaro.Plugins.ContentSearch.Tests.Indexing;

// The index-size cap pauses extraction and writes. These cover what the pause must still do
// (apply the batch's deletions), what it must not do (log the same warning per batch forever) and
// what it must not cost (the skipped files stay discoverable once the cap is raised).
// Captures the process-wide PluginSdk.Logger.LogAction hook, so it must not run concurrently with
// anything that reads or resets it.
[TestClass]
[DoNotParallelize]
public sealed class IndexBatchProcessorIndexCapTests
{
    private string _tempDir = null!;
    private string _tempDbPath = null!;
    private ContentSearchDatabase _database = null!;
    private readonly List<string> _logLines = new();

    [TestInitialize]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "TestBatchCap_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _tempDbPath = Path.Combine(Path.GetTempPath(), "TestBatchCap_" + Guid.NewGuid().ToString("N") + ".db");
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
    public async Task ProcessBatchAsync_OverTheCap_StillAppliesTheBatchesDeletions()
    {
        // Two rows go stale for two different reasons while the cap holds. Both must still go:
        // deletions free index space, and a row whose file left scope can never become valid again.
        var monitoredDir = Path.Combine(_tempDir, "monitored");
        Directory.CreateDirectory(monitoredDir);
        var outOfScope = Path.Combine(_tempDir, "moved.txt");
        await File.WriteAllTextAsync(outOfScope, "indexed before the folder was unmapped");
        _database.InsertOrUpdateFile(outOfScope, DateTime.UtcNow, 40, "indexed before the folder was unmapped");

        var missing = Path.Combine(monitoredDir, "gone.txt");
        _database.InsertOrUpdateFile(missing, DateTime.UtcNow, 40, "indexed before the file vanished");
        _database.UpdateMissingCounts(new Dictionary<string, int> { [missing] = 2 });

        var processor = new IndexBatchProcessor(_database);
        await processor.ProcessBatchAsync(new[] { outOfScope, missing }, MakeConfig(monitoredDir, maxIndexSizeBytes: 1), CancellationToken.None);

        Assert.IsNull(_database.GetFileRecord(outOfScope), "the cap must not keep an out-of-scope row searchable");
        Assert.IsNull(_database.GetFileRecord(missing), "the cap must not keep a row past its missing-file grace");
        Assert.IsEmpty(_database.SearchFts("indexed before", 10));
    }

    [TestMethod]
    public async Task ProcessBatchAsync_OverTheCap_WritesNothingAndLogsThePauseOnce()
    {
        var first = await WriteFileAsync("first.txt", "indexable plain text content");
        var second = await WriteFileAsync("second.txt", "more indexable plain text");
        var processor = new IndexBatchProcessor(_database);
        var config = MakeConfig(_tempDir, maxIndexSizeBytes: 1);

        await processor.ProcessBatchAsync(new[] { first }, config, CancellationToken.None);
        await processor.ProcessBatchAsync(new[] { second }, config, CancellationToken.None);

        Assert.IsNull(_database.GetFileRecord(first), "an over-budget batch writes nothing");
        Assert.IsNull(_database.GetFileRecord(second));
        Assert.HasCount(1, CapWarnings(), $"the paused state is one condition, not one per batch: [{string.Join("; ", CapWarnings())}]");
    }

    [TestMethod]
    public async Task ProcessBatchAsync_CapRaisedAfterAPausedBatch_IndexesTheSkippedFile()
    {
        // The pause must not lose work: a skipped file keeps no row (so discovery, which enqueues
        // every path whose row is absent or outdated, finds it again) and the next scan indexes it
        // once the cap allows it.
        var file = await WriteFileAsync("waiting.txt", "indexable plain text content");
        var processor = new IndexBatchProcessor(_database);

        await processor.ProcessBatchAsync(new[] { file }, MakeConfig(_tempDir, maxIndexSizeBytes: 1), CancellationToken.None);

        Assert.IsNull(_database.GetFileRecord(file), "the paused batch left no row behind");
        Assert.IsFalse(_database.GetAllFileMetadata().ContainsKey(file), "so discovery sees the file as never visited");

        await processor.ProcessBatchAsync(new[] { file }, MakeConfig(_tempDir, maxIndexSizeBytes: long.MaxValue), CancellationToken.None);

        var record = _database.GetFileRecord(file);
        Assert.IsNotNull(record, "raising the cap lets the next scan index the file");
        Assert.IsNull(record.FailedAt);
        Assert.HasCount(1, _database.SearchFts("indexable plain text", 10));
    }

    private List<string> CapWarnings() =>
        _logLines.Where(l => l.Contains("Index size cap reached", StringComparison.Ordinal)).ToList();

    private ContentIndexConfig MakeConfig(string folder, long maxIndexSizeBytes) => new()
    {
        MonitoredFolders = new List<string> { folder },
        AllowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".txt" },
        MaxFileSizeBytes = 1024 * 1024,
        MaxIndexSizeBytes = maxIndexSizeBytes
    };

    private async Task<string> WriteFileAsync(string name, string content)
    {
        var path = Path.Combine(_tempDir, name);
        await File.WriteAllTextAsync(path, content);
        return path;
    }
}
