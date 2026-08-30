using Lertaro.Plugins.ContentSearch.Storage;

namespace Lertaro.Plugins.ContentSearch.Tests.Storage;

// Covers the duplicate-row cascade in DatabaseWriterHelper: rows referencing a source
// are deleted whenever the source is updated or removed, which leaves a search gap for
// those files until the next full scan re-indexes them, so the count must be logged.
// Captures the process-wide PluginSdk.Logger.LogAction hook, so it must not run
// concurrently with anything that reads or resets it.
[TestClass]
[DoNotParallelize]
public sealed class DatabaseWriterHelperTests
{
    private string _tempDbPath = null!;
    private ContentSearchDatabase _database = null!;
    private readonly List<string> _logLines = new();

    [TestInitialize]
    public void SetUp()
    {
        _tempDbPath = Path.Combine(Path.GetTempPath(), "TestWriterHelper_" + Guid.NewGuid().ToString("N") + ".db");
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
    }

    [TestMethod]
    public void InsertOrUpdateBatch_SourceUpdate_CascadesDuplicateRowsAndLogs()
    {
        var source = @"C:\Docs\source.txt";
        var duplicate = @"C:\Docs\duplicate.txt";
        var sourceId = SeedSourceWithDuplicate(source, duplicate, "first content revision");

        _database.InsertOrUpdateBatch(
            [new FileIndexBatchItem(source, DateTime.UtcNow, 40, "second content revision")]);

        Assert.IsNull(_database.GetFileRecord(duplicate),
            "a duplicate referencing the rewritten source must be cascade-deleted");
        Assert.IsNotNull(_database.GetFileRecord(source));
        Assert.IsTrue(
            _logLines.Any(l => l.Contains("1 duplicate row(s) invalidated by source updates", StringComparison.Ordinal)),
            $"Expected an invalidation log line in: [{string.Join("; ", _logLines)}]");
        Assert.AreNotEqual(sourceId, _database.GetFileRecord(source)!.Id,
            "delete+reinsert assigns the source a new row id, which is why duplicates had to go");
    }

    [TestMethod]
    public void DeleteFilesBatch_Source_CascadesDuplicateRowsAndLogs()
    {
        var source = @"C:\Docs\source.txt";
        var duplicate = @"C:\Docs\duplicate.txt";
        SeedSourceWithDuplicate(source, duplicate, "content to be removed");

        _database.DeleteFilesBatch([source]);

        Assert.IsNull(_database.GetFileRecord(source));
        Assert.IsNull(_database.GetFileRecord(duplicate),
            "a duplicate referencing a deleted source must be cascade-deleted");
        Assert.IsTrue(
            _logLines.Any(l => l.Contains("1 duplicate row(s) cascade-deleted with their sources", StringComparison.Ordinal)),
            $"Expected a cascade-deletion log line in: [{string.Join("; ", _logLines)}]");
    }

    [TestMethod]
    public void DeleteFilesBatch_NoDuplicates_NoLog()
    {
        var lone = @"C:\Docs\lone.txt";
        _database.InsertOrUpdateBatch(
            [new FileIndexBatchItem(lone, DateTime.UtcNow, 40, "no duplicates reference me")]);

        _database.DeleteFilesBatch([lone]);

        Assert.IsNull(_database.GetFileRecord(lone));
        Assert.IsFalse(
            _logLines.Any(l => l.Contains("cascade-deleted with their sources", StringComparison.Ordinal)),
            "no cascade happened, so nothing may be logged");
    }

    private long SeedSourceWithDuplicate(string source, string duplicate, string content)
    {
        var idByPath = _database.InsertOrUpdateBatch(
            [new FileIndexBatchItem(source, DateTime.UtcNow, 40, content)]);
        _database.InsertOrUpdateBatch(
            [new FileIndexBatchItem(duplicate, DateTime.UtcNow, 40, string.Empty, ContentRef: idByPath[source])]);
        return idByPath[source];
    }
}
