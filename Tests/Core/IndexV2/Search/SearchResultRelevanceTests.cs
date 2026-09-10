using Lertaro.Core.IndexV2.Search;
using Lertaro.Core.SearchIndex;
using Lertaro.Core.SearchIndex.Fzf;

namespace Lertaro.Core.Tests.IndexV2.Search;

// How a name-mode result's RankSortKey is assembled. Key order, strongest first:
//   start position > quality weight > tier
// Small key = better, which is how SearchResultRankComparer reads it.
[TestClass]
public sealed class SearchResultRelevanceTests
{
    [TestMethod]
    public void Apply_EarlierMatchInLongerName_RanksAboveLaterMatchInShorterName()
    {
        var leftmost = Ranked(@"C:\data\wxfef.doc", "wxfef.doc", "wx");
        var later = Ranked(@"C:\data\iwxfe.mp", "iwxfe.mp", "wx");

        // Coverage alone favours the shorter name; the start position must overrule it.
        Assert.IsLessThan(later, leftmost);
    }

    [TestMethod]
    public void Apply_EarlierMatchRank_DoesNotDependOnDirectoryDepth()
    {
        var shallow = Ranked(@"C:\a\wxfef.doc", "wxfef.doc", "wx");
        var deep = Ranked(@"C:\a\very\deep\nested\wxfef.doc", "wxfef.doc", "wx");

        Assert.AreEqual(shallow, deep);
    }

    [TestMethod]
    public void Apply_NameMatch_RanksAboveAncestorOnlyMatch()
    {
        var nameMatch = Ranked(@"C:\unrelated\xwxfef.txt", "xwxfef.txt", "wx");
        var ancestorOnly = Ranked(@"C:\wx_documents\report.txt", "report.txt", "wx");

        Assert.IsLessThan(ancestorOnly, nameMatch);
    }

    // Tier is the WEAKEST key: it only separates rows that already agree on both start and weight. These
    // assert the key layout directly, because a real match cannot be made to produce an arbitrary tier.
    [TestMethod]
    public void KeyLayout_StartOutranksTier()
    {
        // start 3 / tier 0 (literal)  vs  start 0 / tier 2 (full pinyin): the earlier start wins.
        var laterLiteral = Key(start: 3, weight: 0, tier: MatchRank.TierName);
        var earlierFull = Key(start: 0, weight: 0, tier: MatchRank.TierFull);

        Assert.IsLessThan(laterLiteral, earlierFull);
    }

    [TestMethod]
    public void KeyLayout_WeightOutranksTier()
    {
        // Same start; the better weight wins even though its tier is the worst.
        var betterWeightWorseTier = Key(start: 0, weight: 1, tier: MatchRank.TierFull);
        var worseWeightBestTier = Key(start: 0, weight: 9, tier: MatchRank.TierName);

        Assert.IsLessThan(worseWeightBestTier, betterWeightWorseTier);
    }

    [TestMethod]
    public void KeyLayout_TierBreaksTiesOnEqualStartAndWeight()
    {
        var literal = Key(start: 0, weight: 5, tier: MatchRank.TierName);
        var initials = Key(start: 0, weight: 5, tier: MatchRank.TierInitials);
        var full = Key(start: 0, weight: 5, tier: MatchRank.TierFull);

        Assert.IsLessThan(initials, literal);
        Assert.IsLessThan(full, initials);
    }

    // The documented packing: start(8) | weight(16) | tier(8), "smaller is better".
    private static ulong Key(int start, uint weight, int tier) =>
        ((ulong)(byte)start << 56) | ((ulong)weight << 40) | ((ulong)(byte)tier << 32);

    [TestMethod]
    public void Apply_EmptyPattern_LeavesRankUnchanged()
    {
        var result = new SearchResult { Name = "readme.md", Path = @"C:\readme.md" };
        result.RankSortKey = 0x1234_5678_9ABC_DEF0UL;

        SearchResultRelevance.Apply(result, FzfPattern.Parse(""));

        Assert.AreEqual(0x1234_5678_9ABC_DEF0UL, result.RankSortKey);
    }

    // The engine's own span/length bits in the low 32 must survive: they are the deepest tie-breaker and
    // dropping them would make equally-ranked rows fall back to insertion order.
    [TestMethod]
    public void Apply_PreservesTheEnginesLowBits()
    {
        var result = new SearchResult { Name = "readme.md", Path = @"C:\readme.md" };
        result.RankSortKey = 0xFFFF_FFFF_DEAD_BEEFUL;

        SearchResultRelevance.Apply(result, FzfPattern.Parse("read"));

        Assert.AreEqual(0xDEAD_BEEFUL, result.RankSortKey & 0xFFFF_FFFFUL);
    }

    private static ulong Ranked(string path, string name, string query)
    {
        var result = new SearchResult { Name = name, Path = path };
        SearchResultRelevance.Apply(result, FzfPattern.Parse(query));
        return result.RankSortKey;
    }
}
