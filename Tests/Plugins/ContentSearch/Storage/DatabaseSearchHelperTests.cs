using Lertaro.Plugins.ContentSearch.Storage;

namespace Lertaro.Plugins.ContentSearch.Tests.Storage;

// Regression for the short-token JOIN direction: the query used to self-join the
// source row against its duplicates while selecting only source columns, so duplicate
// files never surfaced and each source's K duplicates ate K+1 rows out of the LIMIT.
[TestClass]
public sealed class DatabaseSearchHelperTests
{
    private string _tempDbPath = null!;
    private ContentSearchDatabase _database = null!;

    [TestInitialize]
    public void SetUp()
    {
        _tempDbPath = Path.Combine(Path.GetTempPath(), "TestSearchHelper_" + Guid.NewGuid().ToString("N") + ".db");
        _database = new ContentSearchDatabase(_tempDbPath);
        _database.Initialize();
    }

    [TestCleanup]
    public void TearDown()
    {
        _database.Dispose();
        if (File.Exists(_tempDbPath))
        {
            try { File.Delete(_tempDbPath); } catch { }
        }
    }

    [TestMethod]
    public void Search_ShortToken_SurfacesSourceAndItsDuplicates()
    {
        // CJK one/two-character terms are the primary short-token case for this app.
        const string sourceText = "duplicate payload with 短词 marker inside";
        var source = @"C:\Docs\source.txt";
        var duplicate = @"C:\Docs\duplicate.txt";
        var sourceId = _database.InsertOrUpdateBatch(
            [new FileIndexBatchItem(source, DateTime.UtcNow, 40, sourceText)])[source];
        _database.InsertOrUpdateBatch(
            [new FileIndexBatchItem(duplicate, DateTime.UtcNow, 40, string.Empty, ContentRef: sourceId)]);

        var hits = _database.SearchFts("词", 10);

        Assert.HasCount(2, hits, $"both the source and the duplicate must surface: [{Describe(hits)}]");
        Assert.IsTrue(hits.Any(h => h.FilePath == source));
        Assert.IsTrue(hits.Any(h => h.FilePath == duplicate));
        // A duplicate owns no text of its own: its snippet reuses the source row's text.
        Assert.IsTrue(hits.Single(h => h.FilePath == duplicate).Snippet.Contains("marker inside", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Search_ShortToken_DuplicatesDoNotEatOtherFilesOutOfTheLimit()
    {
        // With the old self-join, limit=2 returned only the source: the duplicate rows
        // (all pointing at the same source columns) consumed the rest of the LIMIT.
        var sourceId = _database.InsertOrUpdateBatch(
            [new FileIndexBatchItem(@"C:\Docs\source.txt", DateTime.UtcNow, 40, "another 短 marker text")])[@"C:\Docs\source.txt"];
        _database.InsertOrUpdateBatch(
            [new FileIndexBatchItem(@"C:\Docs\dup.txt", DateTime.UtcNow, 40, string.Empty, ContentRef: sourceId)]);
        _database.InsertOrUpdateBatch(
            [new FileIndexBatchItem(@"C:\Docs\other.txt", DateTime.UtcNow, 40, "one more 短 elsewhere")]);

        var hits = _database.SearchFts("短", 2);

        // 2 FTS matches (source, other) expand to 3 files; the old self-join spent both
        // LIMIT rows inside one source's expansion, so "other" could drop out entirely.
        Assert.HasCount(3, hits, $"limit counts FTS matches, not expanded rows: [{Describe(hits)}]");
    }

    private static string Describe(IReadOnlyList<SearchHitItem> hits) =>
        string.Join("; ", hits.Select(h => h.FilePath));
}
