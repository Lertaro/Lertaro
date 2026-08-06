using Lertaro.Core.SearchIndex.Fzf;

namespace Lertaro.Core.Tests.SearchIndex.Fzf;

[TestClass]
public sealed class FzfResultRankTests
{
    [TestMethod]
    public void Compare_SmallerSortKey_SortsBeforeLarger()
    {
        var better = new FzfRank(0, 0, SortKey: 10);
        var worse = new FzfRank(1, 0, SortKey: 20);

        Assert.IsLessThan(0, FzfResultRank.Compare(better, worse));
        Assert.IsGreaterThan(0, FzfResultRank.Compare(worse, better));
    }

    [TestMethod]
    public void Compare_EqualSortKey_TieBreaksByEntryIndex()
    {
        var lowIndex = new FzfRank(EntryIndex: 1, Score: 0, SortKey: 10);
        var highIndex = new FzfRank(EntryIndex: 5, Score: 0, SortKey: 10);

        Assert.IsLessThan(0, FzfResultRank.Compare(lowIndex, highIndex));
    }

    [TestMethod]
    public void ForDefaultScheme_HigherScore_RanksBetterThanLowerScore()
    {
        var match = new FzfPatternResult(Score: 100, MinBegin: 0, MinEnd: 4, MaxEnd: 4, ValidOffsetFound: true);
        var weakMatch = match with { Score = 10 };

        var strong = FzfResultRank.ForDefaultScheme(0, "readme.md", match);
        var weak = FzfResultRank.ForDefaultScheme(1, "readme.md", weakMatch);

        Assert.IsLessThan(0, FzfResultRank.Compare(strong, weak));
    }

    [TestMethod]
    public void ForDefaultScheme_EarlierMatchPosition_RanksBetterThanLaterPosition()
    {
        var text = "aaaareadmeaaaa";
        var early = new FzfPatternResult(Score: 50, MinBegin: 0, MinEnd: 4, MaxEnd: 4, ValidOffsetFound: true);
        var late = new FzfPatternResult(Score: 50, MinBegin: 8, MinEnd: 12, MaxEnd: 12, ValidOffsetFound: true);

        var earlyRank = FzfResultRank.ForDefaultScheme(0, text, early);
        var lateRank = FzfResultRank.ForDefaultScheme(1, text, late);

        Assert.IsLessThan(0, FzfResultRank.Compare(earlyRank, lateRank));
    }

    [TestMethod]
    public void ApplyWeight_FullWeight_LeavesRankUnchanged()
    {
        var match = new FzfPatternResult(Score: 100, MinBegin: 0, MinEnd: 4, MaxEnd: 4, ValidOffsetFound: true);
        var rank = FzfResultRank.ForDefaultScheme(0, "readme.md", match);

        var weighted = FzfResultRank.ApplyWeight(rank, 1.0);

        Assert.AreEqual(rank.SortKey, weighted.SortKey);
    }

    [TestMethod]
    public void ApplyWeight_PartialWeight_MakesRankWorse()
    {
        var match = new FzfPatternResult(Score: 100, MinBegin: 0, MinEnd: 4, MaxEnd: 4, ValidOffsetFound: true);
        var rank = FzfResultRank.ForDefaultScheme(0, "readme.md", match);

        var weighted = FzfResultRank.ApplyWeight(rank, 0.5);

        // Reducing effective score makes the SortKey larger (worse rank) -- ApplyWeight only rewrites
        // the score component, so the two are directly comparable via FzfResultRank.Compare.
        Assert.IsGreaterThan(0, FzfResultRank.Compare(weighted, rank));
    }

    [TestMethod]
    public void RankLow32_SameTextAndMatch_IsDeterministic()
    {
        var match = new FzfPatternResult(Score: 50, MinBegin: 2, MinEnd: 6, MaxEnd: 6, ValidOffsetFound: true);

        var first = FzfResultRank.RankLow32("readme.md", match);
        var second = FzfResultRank.RankLow32("readme.md", match);

        Assert.AreEqual(first, second);
    }
}
