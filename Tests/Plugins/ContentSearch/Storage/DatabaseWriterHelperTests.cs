using Lertaro.Plugins.ContentSearch.Storage;

namespace Lertaro.Plugins.ContentSearch.Tests.Storage;

// Covers the duplicate-row cascade in DatabaseWriterHelper: rows referencing a source
// are deleted whenever the source is updated or removed, which leaves a search gap for
// those files until the next full scan re-indexes them, so the count must be logged.
// [DoNotParallelize]: the process-wide PluginSdk.Logger.LogAction hook -- see IndexBatchProcessorTests' header.
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

    // Windows paths are case-insensitive: after a case-only folder rename (Docs -> docs) the host
    // index reports an edited file in its new casing. That must replace the file's row, not add a
    // second one that keeps the old text searchable and that the scan's case-insensitive
    // dictionaries hide from missing-file and out-of-scope pruning.
    [TestMethod]
    public void InsertOrUpdateBatch_SameFileInOtherCasing_ReplacesTheRowWithTheNewCasing()
    {
        _database.InsertOrUpdateFile(@"X:\Docs\a.txt", DateTime.UtcNow, 10, "obsolete wording");
        _database.InsertOrUpdateFile(@"X:\docs\a.txt", DateTime.UtcNow, 12, "current wording");

        Assert.AreEqual(1, _database.GetStats().TotalFiles, "one file, one row");
        Assert.AreEqual(@"X:\docs\a.txt", _database.GetFileRecord(@"X:\docs\a.txt")!.Path, "the row follows the newest casing");
        Assert.IsEmpty(_database.SearchFts("obsolete", 10));
        Assert.HasCount(1, _database.SearchFts("wording", 10));
    }

    // The writer replaces the file's row in any casing, so the dedup lookup must not offer that
    // same row as the file's "source": the new row would reference an id deleted a moment later.
    [TestMethod]
    public void FindIndexedSourceByHash_OwnRowInOtherCasing_IsNotASource()
    {
        _database.InsertOrUpdateBatch(
            [new FileIndexBatchItem(@"X:\Docs\big.bin", DateTime.UtcNow, 40, "text", ContentHash: "AB12")]);

        Assert.IsNull(_database.FindIndexedSourceByHash("AB12", @"X:\docs\big.bin"));
    }

    // Callers holding the other casing (discovery after a case-only rename) must reach the row.
    [TestMethod]
    public void PathLookups_IgnoreAsciiCase()
    {
        _database.InsertOrUpdateFile(@"X:\Docs\a.txt", DateTime.UtcNow, 10, "text");

        _database.UpdateMissingCounts(new Dictionary<string, int> { [@"x:\DOCS\a.txt"] = 2 });
        Assert.AreEqual(2, _database.GetFileRecord(@"x:\docs\A.TXT")?.MissingCount);

        _database.DeleteFilesBatch([@"X:\DOCS\A.txt"]);
        Assert.AreEqual(0, _database.GetStats().TotalFiles);
    }

    // The per-file lookup runs once per written file against up to ~1M rows: it must be an index
    // search, never a scan.
    [TestMethod]
    public void InsertOrUpdateBatch_PathLookup_IsAnIndexSearch()
    {
        using var conn = _database.OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "EXPLAIN QUERY PLAN " + DatabaseWriterHelper.FindIdsByPathSql;
        cmd.Parameters.AddWithValue("@path", @"X:\docs\a.txt");
        using var reader = cmd.ExecuteReader();
        var plan = new List<string>();
        while (reader.Read())
            plan.Add(reader.GetString(3));

        Assert.IsTrue(plan.Any(p => p.Contains("USING COVERING INDEX idx_files_path_nocase", StringComparison.Ordinal)),
            string.Join("; ", plan));
    }

    // Databases written before the fix may already hold one row per casing. Initialize deletes
    // every row of such a group once, through the normal delete (FTS text and rows reusing their
    // content go with them), and the next scan re-indexes the file in its current casing.
    [TestMethod]
    public void Initialize_ExistingCaseDuplicates_AreDeletedForReindexing()
    {
        _database.InsertOrUpdateFile(@"X:\Docs\a.txt", DateTime.UtcNow, 10, "obsolete wording");
        _database.InsertOrUpdateFile(@"X:\other.txt", DateTime.UtcNow, 10, "unrelated text");
        using (var conn = _database.OpenConnection())
        using (var seed = conn.CreateCommand())
        {
            // What the old exact-case writer left behind, plus a duplicate reusing the stale row.
            seed.CommandText = """
                INSERT INTO files (path, last_modified, file_size, indexed_at) VALUES ('X:\docs\a.txt', 0, 12, 0);
                INSERT INTO files_fts (rowid, content) VALUES (last_insert_rowid(), 'current wording');
                INSERT INTO files (path, last_modified, file_size, indexed_at, content_ref)
                    SELECT 'X:\copy.txt', 0, 10, 0, id FROM files WHERE path = 'X:\Docs\a.txt';
                PRAGMA user_version = 0;
                """;
            seed.ExecuteNonQuery();
        }

        using var reopened = new ContentSearchDatabase(_tempDbPath);
        reopened.Initialize();

        Assert.AreEqual(1, reopened.GetStats().TotalFiles);
        Assert.IsNotNull(reopened.GetFileRecord(@"X:\other.txt"));
        Assert.IsEmpty(reopened.SearchFts("wording", 10));
        using var check = reopened.OpenConnection();
        using var ftsCount = check.CreateCommand();
        ftsCount.CommandText = "SELECT COUNT(*) FROM files_fts;";
        Assert.AreEqual(1L, ftsCount.ExecuteScalar(), "no FTS text may outlive its row");
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
