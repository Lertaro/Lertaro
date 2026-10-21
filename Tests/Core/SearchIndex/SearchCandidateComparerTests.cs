using Lertaro.Core.IndexV2.Search;
using Lertaro.Core.SearchIndex;

namespace Lertaro.Core.Tests.SearchIndex;

// What the engine keeps when a search has to drop rows: match quality, then the shared path tail. This is
// the same sequence the full window's comparer degrades to with no history, no favorites and no priority
// rules, which is why swapping the truncation over to it changes nothing about which rows survive.
[TestClass]
public sealed class SearchCandidateComparerTests
{
    private static SearchResult Row(string path, string? name = null, ulong key = 0) =>
        new()
        {
            Name = name ?? System.IO.Path.GetFileName(path),
            Path = path,
            IsDir = false,
            Drive = "D",
            RankSortKey = key,
        };

    // Only the high 32 bits carry quality, and every field in them is "smaller is better".
    private static ulong QualityKey(MatchRank rank, ulong engineTie = 1UL) =>
        (ulong)SearchResultRelevance.HighKey(rank) << 32 | engineTie;

    private static List<string> Order(params SearchResult[] rows)
    {
        var list = rows.ToList();
        list.Sort(SearchCandidateComparer.Instance);
        return list.Select(r => r.FullPath).ToList();
    }

    [TestMethod]
    public void Compare_HigherPackedQuality_ComesFirst()
    {
        var exact = QualityKey(new MatchRank(MatchRank.TierName, 0, 1d, 1d, MatchRank.ShapeWholeWord));
        var scattered = QualityKey(new MatchRank(MatchRank.TierName, 0, 1d, 0.2d, MatchRank.ShapeScattered));

        // The better match has the numerically SMALLER key; a drive that answered only scattered rows must
        // not push an exact hit out of the kept set.
        CollectionAssert.AreEqual(
            new[] { @"D:\dir\exact.txt", @"D:\dir\scattered.txt" },
            Order(
                Row(@"D:\dir\scattered.txt", key: scattered),
                Row(@"D:\dir\exact.txt", key: exact)));
    }

    [TestMethod]
    public void Compare_SameQuality_FallsToNameThenPath()
    {
        var quality = QualityKey(new MatchRank(MatchRank.TierName, 0, 1d, 1d, MatchRank.ShapeWholeWord));

        CollectionAssert.AreEqual(
            new[] { @"D:\dir\second", @"D:\dir\tenth" },
            Order(
                Row(@"D:\dir\tenth", name: "a10", key: quality),
                Row(@"D:\dir\second", name: "a2", key: quality)));

        CollectionAssert.AreEqual(
            new[] { @"D:\p\a.txt", @"D:\pp\a.txt" },
            Order(
                Row(@"D:\pp\a.txt", key: quality),
                Row(@"D:\p\a.txt", key: quality)));
    }

    [TestMethod]
    public void Compare_FullKeyBreaksFinalTie()
    {
        // Identical rows except for the engine's own low 32 bits: the smaller one is the one the engine
        // itself would have emitted first, and it stays first.
        var early = QualityKey(new MatchRank(MatchRank.TierName, 0, 1d, 1d, MatchRank.ShapeWholeWord), engineTie: 5);
        var late = QualityKey(new MatchRank(MatchRank.TierName, 0, 1d, 1d, MatchRank.ShapeWholeWord), engineTie: 9);

        Assert.IsLessThan(0, SearchCandidateComparer.Instance.Compare(Row(@"D:\dir\a.txt", key: early), Row(@"D:\dir\a.txt", key: late)));
        Assert.AreEqual(0, SearchCandidateComparer.Instance.Compare(Row(@"D:\dir\a.txt", key: early), Row(@"D:\dir\a.txt", key: early)));
    }
}
