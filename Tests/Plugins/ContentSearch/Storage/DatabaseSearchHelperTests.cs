using Lertaro.Plugins.ContentSearch.Storage;
using Microsoft.Data.Sqlite;

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

    [TestMethod]
    public void Search_ShortToken_EachDuplicateReusesItsOwnSourcesSnippet()
    {
        // The short-token path memoizes the snippet per matched source, so a source with many duplicates
        // has its text scanned once instead of once per duplicate. Memoize on the wrong key and every
        // duplicate quietly renders another file's excerpt -- which is what this pins.
        var sourceA = @"C:\Docs\a.txt";
        var sourceB = @"C:\Docs\b.txt";
        var idA = _database.InsertOrUpdateBatch(
            [new FileIndexBatchItem(sourceA, DateTime.UtcNow, 30, "alpha 猫 excerpt")])[sourceA];
        var idB = _database.InsertOrUpdateBatch(
            [new FileIndexBatchItem(sourceB, DateTime.UtcNow, 30, "bravo 猫 excerpt")])[sourceB];
        _database.InsertOrUpdateBatch(
            [new FileIndexBatchItem(@"C:\Docs\a-copy.txt", DateTime.UtcNow, 30, string.Empty, ContentRef: idA)]);
        _database.InsertOrUpdateBatch(
            [new FileIndexBatchItem(@"C:\Docs\b-copy.txt", DateTime.UtcNow, 30, string.Empty, ContentRef: idB)]);

        var hits = _database.SearchFts("猫", 10);

        Assert.HasCount(4, hits, $"[{Describe(hits)}]");
        foreach (var (copyName, ownWord, otherWord) in new[]
                 {
                     ("a-copy", "alpha", "bravo"),
                     ("b-copy", "bravo", "alpha"),
                 })
        {
            var snippet = hits.Single(h => h.FileName == $"{copyName}.txt").Snippet;
            Assert.IsTrue(snippet.Contains(ownWord, StringComparison.Ordinal), $"{copyName} lost its own source: {snippet}");
            Assert.IsFalse(snippet.Contains(otherWord, StringComparison.Ordinal), $"{copyName} borrowed another source: {snippet}");
        }
    }

    private static string Describe(IReadOnlyList<SearchHitItem> hits) =>
        string.Join("; ", hits.Select(h => h.FilePath));
}

// The FTS path (tokens of three or more characters), which is what a normal "cs xxx" query takes.
//
// Its duplicate expansion is a UNION ALL of two joins -- one per way a row can belong to a match. Each
// half used to be a single `f.id = src OR f.content_ref = src`, which no index can serve, so SQLite
// rescanned the whole `files` table once per hit: quadratic in the 2000 the full search window asks for,
// and enough to freeze the UI thread on a large index. These pin the two halves and the ranking that
// survived being split.
[TestClass]
public sealed class DatabaseSearchHelperFtsTests
{
    private string _tempDbPath = null!;
    private ContentSearchDatabase _database = null!;

    [TestInitialize]
    public void SetUp()
    {
        _tempDbPath = Path.Combine(Path.GetTempPath(), "TestSearchFts_" + Guid.NewGuid().ToString("N") + ".db");
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
    public void Search_FtsToken_SurfacesSourceAndItsDuplicates()
    {
        const string source = @"C:\Docs\report-source.txt";
        const string duplicate = @"C:\Docs\report-copy.txt";
        var sourceId = _database.InsertOrUpdateBatch(
            [new FileIndexBatchItem(source, DateTime.UtcNow, 44, "the quarterly report covers the migration plan")])[source];
        _database.InsertOrUpdateBatch(
            [new FileIndexBatchItem(duplicate, DateTime.UtcNow, 44, string.Empty, ContentRef: sourceId)]);

        var hits = _database.SearchFts("report", 10);

        Assert.HasCount(2, hits, $"both halves of the expansion must be present: [{Describe(hits)}]");
        Assert.IsTrue(hits.Any(h => h.FilePath == source));
        Assert.IsTrue(hits.Any(h => h.FilePath == duplicate));
        // A duplicate owns no text of its own: it carries the snippet FTS5 generated for the source row.
        var sourceSnippet = hits.Single(h => h.FilePath == source).Snippet;
        Assert.IsTrue(sourceSnippet.Contains("report", StringComparison.Ordinal), $"the match belongs in the snippet: {sourceSnippet}");
        Assert.AreEqual(sourceSnippet, hits.Single(h => h.FilePath == duplicate).Snippet, "the duplicate must reuse the source snippet");
    }

    [TestMethod]
    public void Search_FtsToken_DuplicatesDoNotEatOtherFilesOutOfTheLimit()
    {
        var sourceId = _database.InsertOrUpdateBatch(
            [new FileIndexBatchItem(@"C:\Docs\a.txt", DateTime.UtcNow, 30, "alpha report body")])[@"C:\Docs\a.txt"];
        _database.InsertOrUpdateBatch(
            [new FileIndexBatchItem(@"C:\Docs\a-copy.txt", DateTime.UtcNow, 30, string.Empty, ContentRef: sourceId)]);
        _database.InsertOrUpdateBatch(
            [new FileIndexBatchItem(@"C:\Docs\b.txt", DateTime.UtcNow, 30, "beta report body")]);

        var hits = _database.SearchFts("report", 2);

        // Two FTS matches expand to three files: LIMIT caps the matches, not the expansion, so one
        // source having a duplicate must not push an unrelated file out of the answer.
        Assert.HasCount(3, hits, $"[{Describe(hits)}]");
    }

    [TestMethod]
    public void Search_FtsToken_KeepsTheBestRankedMatchFirst()
    {
        const string strong = @"C:\Docs\strong.txt";
        const string weak = @"C:\Docs\weak.txt";
        var filler = string.Concat(Enumerable.Repeat("lorem ipsum dolor sit amet consectetur ", 60));
        _database.InsertOrUpdateBatch([new FileIndexBatchItem(strong, DateTime.UtcNow, 6, "report")]);
        _database.InsertOrUpdateBatch([new FileIndexBatchItem(weak, DateTime.UtcNow, 2000, $"report {filler}")]);

        var hits = _database.SearchFts("report", 10);

        // A compound SELECT answers in whatever order its halves finish, so the split needed the rank
        // re-applied across the whole result. bm25's length normalization puts the one-word document first.
        Assert.HasCount(2, hits, $"[{Describe(hits)}]");
        Assert.AreEqual(strong, hits[0].FilePath, $"[{Describe(hits)}]");
    }

    [TestMethod]
    public void Search_FtsToken_MultipleTermsStillRequireThemAll()
    {
        _database.InsertOrUpdateBatch([new FileIndexBatchItem(@"C:\Docs\both.txt", DateTime.UtcNow, 40, "quarterly report draft")]);
        _database.InsertOrUpdateBatch([new FileIndexBatchItem(@"C:\Docs\only-one.txt", DateTime.UtcNow, 40, "quarterly budget draft")]);

        var hits = _database.SearchFts("quarterly report", 10);

        Assert.HasCount(1, hits, $"the AND the query builder emits must survive the split: [{Describe(hits)}]");
        Assert.AreEqual(@"C:\Docs\both.txt", hits[0].FilePath);
    }

    [TestMethod]
    public void Initialize_IndexesContentRef_WhichIsWhatTheDuplicateHalfSeeksOn()
    {
        // Without this index the duplicate half of the expansion is a scan of `files` per hit -- the
        // difference between a settled search and one that holds the UI thread for minutes.
        using var conn = new SqliteConnection($"Data Source={_tempDbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name FROM pragma_index_list('files');";
        using var reader = cmd.ExecuteReader();
        var indexes = new List<string>();
        while (reader.Read())
            indexes.Add(reader.GetString(0));

        CollectionAssert.Contains(indexes, "idx_files_content_ref");
    }

    private static string Describe(IReadOnlyList<SearchHitItem> hits) =>
        string.Join("; ", hits.Select(h => h.FilePath));
}
