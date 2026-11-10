using Lertaro.Core.IndexV2.Search;
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
}
