using Lertaro.Core.IndexV2.Search;
using Lertaro.Core.SearchIndex;
using Lertaro.PluginSdk.Helpers;

namespace Lertaro.Core.Tests.IndexV2.Search;

[TestClass]
public sealed class QueryPageCacheTests
{
    private static SearchResult Row(string name) => new()
    {
        Name = name,
        Path = @"D:\cache\" + name,
        Drive = "D",
    };

    [TestMethod]
    public void ReplaysACompletedCategoryPageForTheSameRevision()
    {
        var cache = new QueryPageCache();
        var key = QueryPageCache.KeyFor("a", null, null, SearchCategoryFilter.Video);
        cache.Store(key, [Row("a.mp4")], requestLimit: 10, revision: 7);

        var replay = new List<SearchResult>();
        Assert.IsTrue(cache.TryReplay(key, 10, replay.Add, CancellationToken.None, revision: 7));
        Assert.AreEqual(1, replay.Count);
    }

    [TestMethod]
    public void DifferentCategoryCannotReuseThePage()
    {
        var cache = new QueryPageCache();
        var video = QueryPageCache.KeyFor("a", null, null, SearchCategoryFilter.Video);
        var audio = QueryPageCache.KeyFor("a", null, null, SearchCategoryFilter.Audio);
        cache.Store(video, [Row("a.mp4")], requestLimit: 10, revision: 7);

        Assert.IsFalse(cache.TryReplay(audio, 10, _ => { }, CancellationToken.None, revision: 7));
    }

    [TestMethod]
    public void RequestMatchModesCannotReuseThePage()
    {
        var previousFuzzy = SearchContext.FuzzyMatchEnabled;
        var previousPrecedence = SearchContext.AndFirstPrecedence;
        var previousDisabledAliases = SearchContext.DisabledAliasIds;
        try
        {
            SearchContext.FuzzyMatchEnabled = true;
            SearchContext.AndFirstPrecedence = true;
            SearchContext.DisabledAliasIds = null;
            var fuzzy = QueryPageCache.KeyFor("a", null, null, SearchCategoryFilter.All);

            SearchContext.FuzzyMatchEnabled = false;
            var exact = QueryPageCache.KeyFor("a", null, null, SearchCategoryFilter.All);
            SearchContext.AndFirstPrecedence = false;
            var orFirst = QueryPageCache.KeyFor("a", null, null, SearchCategoryFilter.All);
            SearchContext.DisabledAliasIds = [7];
            var aliasesDisabled = QueryPageCache.KeyFor("a", null, null, SearchCategoryFilter.All);

            Assert.AreNotEqual(fuzzy, exact);
            Assert.AreNotEqual(exact, orFirst);
            Assert.AreNotEqual(orFirst, aliasesDisabled);
        }
        finally
        {
            SearchContext.FuzzyMatchEnabled = previousFuzzy;
            SearchContext.AndFirstPrecedence = previousPrecedence;
            SearchContext.DisabledAliasIds = previousDisabledAliases;
        }
    }

    [TestMethod]
    public void AliasProviderQueryRevisionInvalidatesThePage()
    {
        var before = QueryPageCache.KeyFor("a", null, null, SearchCategoryFilter.All);
        AliasProviderRegistry.InvalidateQueryPlans();
        var after = QueryPageCache.KeyFor("a", null, null, SearchCategoryFilter.All);

        Assert.AreNotEqual(before, after);
    }

    [TestMethod]
    public void CacheKeyIncludesQueryDirectoryAndFileFilters()
    {
        var baseline = QueryPageCache.KeyFor(
            "report", @"D:\work", "*.pdf", SearchCategoryFilter.All);

        Assert.AreNotEqual(baseline, QueryPageCache.KeyFor(
            "reports", @"D:\work", "*.pdf", SearchCategoryFilter.All));
        Assert.AreNotEqual(baseline, QueryPageCache.KeyFor(
            "report", @"D:\other", "*.pdf", SearchCategoryFilter.All));
        Assert.AreNotEqual(baseline, QueryPageCache.KeyFor(
            "report", @"D:\work", "*.docx", SearchCategoryFilter.All));
        Assert.AreNotEqual(baseline, QueryPageCache.KeyFor(
            "report", @"D:\work", "*.pdf", SearchCategoryFilter.Document));
    }

    [TestMethod]
    public void IndexRevisionChangeInvalidatesThePage()
    {
        var cache = new QueryPageCache();
        var key = QueryPageCache.KeyFor("a", null, null, SearchCategoryFilter.Audio);
        cache.Store(key, [Row("a.mp3")], requestLimit: 10, revision: 7);

        Assert.IsFalse(cache.TryReplay(key, 10, _ => { }, CancellationToken.None, revision: 8));
    }

    [TestMethod]
    public void AnExhaustedEmptyPageStillAnswersTheRequest()
    {
        var cache = new QueryPageCache();
        var key = QueryPageCache.KeyFor("a", null, null, SearchCategoryFilter.Audio);
        cache.Store(key, [], requestLimit: 10, revision: 7);

        Assert.IsTrue(cache.TryReplay(key, 10, _ => { }, CancellationToken.None, revision: 7));
    }

    [TestMethod]
    public void OversizedPageIsBoundedButItsFirstDisplayPageRemainsReplayable()
    {
        var cache = new QueryPageCache();
        var key = QueryPageCache.KeyFor("a", null, null, SearchCategoryFilter.All);
        var rows = Enumerable.Range(0, QueryPageCache.MaxRows + 1).Select(i => Row($"a{i}")).ToList();
        cache.Store(key, rows, requestLimit: QueryPageCache.MaxRows + 1, revision: 7);

        var replay = new List<SearchResult>();
        Assert.IsTrue(cache.TryReplay(key, 51, replay.Add, CancellationToken.None, revision: 7));
        Assert.HasCount(51, replay);
        Assert.IsFalse(cache.TryReplay(key, QueryPageCache.MaxRows + 1, _ => { }, CancellationToken.None, revision: 7));
    }

    [TestMethod]
    public void CompletePageWhoseCaptureWasTruncatedOnlyReplaysWithinTheStoredRows()
    {
        var cache = new QueryPageCache();
        var key = QueryPageCache.KeyFor("a", null, null, SearchCategoryFilter.All);
        var stored = Enumerable.Range(0, QueryPageCache.MaxRows).Select(i => Row($"a{i}")).ToList();
        cache.Store(
            key, stored, requestLimit: QueryPageCache.MaxRows + 500, revision: 7,
            totalRows: QueryPageCache.MaxRows + 200);

        Assert.IsTrue(cache.TryReplay(
            key, QueryPageCache.MaxRows, _ => { }, CancellationToken.None, revision: 7));
        Assert.IsFalse(cache.TryReplay(
            key, QueryPageCache.MaxRows + 1, _ => { }, CancellationToken.None, revision: 7));
    }

    [TestMethod]
    public void CachedRowsCannotBePollutedByTheCaller()
    {
        var cache = new QueryPageCache();
        var key = QueryPageCache.KeyFor("a", null, null, SearchCategoryFilter.All);
        var first = Row("a.txt");
        first.RankSortKey = 42;
        cache.Store(key, [first], requestLimit: 10, revision: 7);

        first.RankSortKey = ulong.MaxValue;
        var firstReplay = new List<SearchResult>();
        Assert.IsTrue(cache.TryReplay(key, 10, firstReplay.Add, CancellationToken.None, revision: 7));
        Assert.AreEqual(42UL, firstReplay[0].RankSortKey);

        firstReplay[0].RankSortKey = ulong.MaxValue;
        var secondReplay = new List<SearchResult>();
        Assert.IsTrue(cache.TryReplay(key, 10, secondReplay.Add, CancellationToken.None, revision: 7));
        Assert.AreEqual(42UL, secondReplay[0].RankSortKey);
    }

    [TestMethod]
    public void ClearDropsEveryPage()
    {
        var cache = new QueryPageCache();
        var key = QueryPageCache.KeyFor("a", null, null, SearchCategoryFilter.All);
        cache.Store(key, [Row("a.txt")], requestLimit: 10, revision: 7);

        cache.Clear();

        Assert.AreEqual(0, cache.RetainedEntryCount);
        Assert.IsFalse(cache.TryReplay(key, 10, _ => { }, CancellationToken.None, revision: 7));
    }

    [TestMethod]
    public void TotalRowBudgetEvictsOldestPages()
    {
        var cache = new QueryPageCache();
        var rows = Enumerable.Range(0, QueryPageCache.MaxRows).Select(i => Row($"a{i}")).ToList();
        for (var i = 0; i < 33; i++)
        {
            // clone:false reuses the detached list; the cache only retains row references.
            cache.Store($"q{i}", rows, requestLimit: QueryPageCache.MaxRows, revision: 7, clone: false);
        }

        Assert.AreEqual(QueryPageCache.MaxTotalRows, cache.RetainedRowCount);
        Assert.AreEqual(32, cache.RetainedEntryCount);
        Assert.IsFalse(cache.TryReplay("q0", QueryPageCache.MaxRows, _ => { }, CancellationToken.None, revision: 7));
        Assert.IsTrue(cache.TryReplay("q32", QueryPageCache.MaxRows, _ => { }, CancellationToken.None, revision: 7));
    }
}
