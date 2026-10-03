using Lertaro.Plugins.ContentSearch.Storage;

namespace Lertaro.Plugins.ContentSearch.Tests.Storage;

// Regression for the branch that picks between the trigram index and the content scan. The index
// holds three-character sequences only, so a query mixing a short term with a long one
// ("cs 报告单 ab") used to take the index branch, where the short term's half of the AND matched
// nothing: the whole query came back empty although the document matched both terms. Split from
// DatabaseSearchHelperTests to keep both files under the repository's per-file line limit.
[TestClass]
public sealed class DatabaseSearchHelperTermMixTests
{
    private string _tempDbPath = null!;
    private ContentSearchDatabase _database = null!;

    [TestInitialize]
    public void SetUp()
    {
        _tempDbPath = Path.Combine(Path.GetTempPath(), "TestSearchMix_" + Guid.NewGuid().ToString("N") + ".db");
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
    public void RequiresContentScan_AnyTermShorterThanThreeCharacters_ReturnsTrue()
    {
        Assert.IsTrue(DatabaseSearchHelper.RequiresContentScan(["报告单", "ab"]), "the mixed query the reported bug was about");
        Assert.IsTrue(DatabaseSearchHelper.RequiresContentScan(["ab"]));
        Assert.IsTrue(DatabaseSearchHelper.RequiresContentScan(["report", "a"]), "one short term is enough to need the scan");

        Assert.IsFalse(DatabaseSearchHelper.RequiresContentScan(["report"]));
        Assert.IsFalse(DatabaseSearchHelper.RequiresContentScan(["report", "quarterly"]), "all-long queries keep the index path");
        Assert.IsFalse(DatabaseSearchHelper.RequiresContentScan([]), "no terms at all: the caller stops before either path");
    }

    [TestMethod]
    public void Search_ShortTermAndLongTerm_ReturnsTheDocumentMatchingBoth()
    {
        // The reported false negative: the document holds both terms, so the AND of them must hit.
        _database.InsertOrUpdateBatch(
            [new FileIndexBatchItem(@"C:\Docs\report.txt", DateTime.UtcNow, 40, "quarterly 报告单 ab summary")]);

        var hits = _database.SearchFts("报告单 ab", 10);

        Assert.HasCount(1, hits, $"a mixed query must not silently answer nothing: [{Describe(hits)}]");
        Assert.AreEqual(@"C:\Docs\report.txt", hits[0].FilePath);
    }

    [TestMethod]
    public void Search_ShortTermAndLongTerm_ReturnsNothingWhenOnlyOneTermMatches()
    {
        // The scan the mixed query falls to still ANDs every term: dropping the short one would
        // answer this with a hit it must not have.
        _database.InsertOrUpdateBatch(
            [new FileIndexBatchItem(@"C:\Docs\only-long.txt", DateTime.UtcNow, 40, "quarterly 报告单 summary")]);

        var hits = _database.SearchFts("报告单 ab", 10);

        Assert.IsEmpty(hits, $"[{Describe(hits)}]");
    }

    [TestMethod]
    public void Search_AllLongTerms_IsStillAnsweredByTheTrigramIndex()
    {
        // The index ranks hits by bm25 and scores each one differently; the content scan hands rows
        // back in insertion order and scores every hit 1.0. Inserting the best match last makes the
        // two outcomes differ, so this pins that an all-long query still takes the fast index path.
        var filler = string.Concat(Enumerable.Repeat("lorem ipsum dolor sit amet consectetur ", 60));
        _database.InsertOrUpdateBatch(
            [new FileIndexBatchItem(@"C:\Docs\lengthy.txt", DateTime.UtcNow, 2000, $"report {filler}")]);
        _database.InsertOrUpdateBatch(
            [new FileIndexBatchItem(@"C:\Docs\terse.txt", DateTime.UtcNow, 6, "report")]);

        var hits = _database.SearchFts("report", 10);

        Assert.HasCount(2, hits, $"[{Describe(hits)}]");
        Assert.AreEqual(@"C:\Docs\terse.txt", hits[0].FilePath,
            "bm25 puts the one-word document first, insertion order (the content scan) would not");
        Assert.AreNotEqual(hits[0].Score, hits[1].Score, "the index ranks its hits; the content scan scores them all 1.0");
    }

    private static string Describe(IReadOnlyList<SearchHitItem> hits) =>
        string.Join("; ", hits.Select(h => h.FilePath));
}
