using Lertaro.Core.IndexV2.Search;
using Lertaro.Core.SearchIndex.Fzf;
using Lertaro.Core.Tests.IndexV2;

namespace Lertaro.Core.Tests.IndexV2.Search;

[TestClass]
public sealed class QueryFrontierCacheTests
{
    private static UniqueMatch Hit(int uid) => new(uid, default, 0);

    [TestMethod]
    public void ExtendedPlainQueryReusesThePreviousSuperset()
    {
        var cache = new QueryFrontierCache();
        cache.Store("a", revision: 7, sequence: 1, [Hit(2), Hit(9)]);

        CollectionAssert.AreEqual(new[] { 2, 9 }, cache.TryGetSuperset("ap", revision: 7));
        CollectionAssert.AreEqual(new[] { 2, 9 }, cache.TryGetSuperset("apex", revision: 7));
    }

    [TestMethod]
    public void RevisionChangeAndUnsafeQueriesDoNotReuseTheFrontier()
    {
        var cache = new QueryFrontierCache();
        cache.Store("a", revision: 7, sequence: 1, [Hit(2)]);

        Assert.IsNull(cache.TryGetSuperset("ap", revision: 8));

        cache.Store("a p", revision: 8, sequence: 2, [Hit(3)]);
        Assert.IsNull(cache.TryGetSuperset("a pe", revision: 8));
    }

    [TestMethod]
    public void AStaleCompletedSearchCannotReplaceANewerFrontier()
    {
        var cache = new QueryFrontierCache();
        cache.Store("apex", revision: 7, sequence: 2, [Hit(4)]);
        cache.Store("a", revision: 7, sequence: 1, [Hit(1), Hit(2), Hit(3)]);

        CollectionAssert.AreEqual(new[] { 4 }, cache.TryGetSuperset("apex2", revision: 7));
    }

    [TestMethod]
    public void MatchingWithinTheFrontierFindsTheSameNamesAsAFullScan()
    {
        using var fixture = LiveIndexFixture.Build("D",
        [
            LiveIndexFixture.Root(),
            new FileRecord(2, 1, "apple.txt", FileRecordFlags.None),
            new FileRecord(3, 1, "apricot.txt", FileRecordFlags.None),
            new FileRecord(4, 1, "banana.txt", FileRecordFlags.None),
            new FileRecord(5, 1, "a---p.txt", FileRecordFlags.None),
        ]);
        var first = SearchMatcher.RentHitList();
        var subset = SearchMatcher.RentHitList();
        var full = SearchMatcher.RentHitList();
        try
        {
            fixture.Index.Read<object?>((snapshot, _) =>
            {
                SearchMatcher.MatchUniques(snapshot, FzfPattern.Parse("a"), first);
                var frontier = first.Select(hit => hit.Uid).ToArray();
                SearchMatcher.MatchUniques(
                    snapshot, FzfPattern.Parse("ap"), subset, candidateUids: frontier);
                SearchMatcher.MatchUniques(snapshot, FzfPattern.Parse("ap"), full);
                return null;
            });

            CollectionAssert.AreEquivalent(
                full.Select(hit => hit.Uid).ToArray(),
                subset.Select(hit => hit.Uid).ToArray());
        }
        finally
        {
            SearchMatcher.ReturnHitList(first);
            SearchMatcher.ReturnHitList(subset);
            SearchMatcher.ReturnHitList(full);
        }
    }
}
