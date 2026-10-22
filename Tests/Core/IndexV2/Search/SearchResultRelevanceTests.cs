using Lertaro.Core.IndexV2.Search;
using Lertaro.Core.SearchIndex;
using Lertaro.Core.SearchIndex.Fzf;

namespace Lertaro.Core.Tests.IndexV2.Search;

// How a name-mode result's RankSortKey is assembled. The high 32 bits use exactly the same quality
// sequence as the quick window (MatchRank.CompareQuality): left-contiguous, contiguity, start, coverage,
// tier. History is compared after these bits by SearchResultRankComparer, not packed here.
[TestClass]
public sealed class SearchResultRelevanceTests
{
    [TestMethod]
    public void Apply_LeftContiguousHitRanksAboveLaterStart()
    {
        var atStart = Ranked(@"C:\data\atlas.txt", "atlas.txt", "atlas");
        var later = Ranked(@"C:\data\code-atlas", "code-atlas", "atlas");

        Assert.IsLessThan(later, atStart);
    }

    [TestMethod]
    public void Apply_ContiguityRanksAboveAnEarlierScatteredHit()
    {
        var contiguousLater = Ranked(@"C:\data\xxatlas", "xxatlas", "atlas");
        var scatteredAtStart = Ranked(@"C:\data\axtlaxs", "axtlaxs", "atlas");

        Assert.IsLessThan(scatteredAtStart, contiguousLater);
    }

    [TestMethod]
    public void Apply_StartRanksAboveCoverage()
    {
        var earlyLong = Ranked(@"C:\data\xatlasxxxxx", "xatlasxxxxx", "atlas");
        var later = Ranked(@"C:\data\xxxxxatlas", "xxxxxatlas", "atlas");

        Assert.IsLessThan(later, earlyLong);
    }

    [TestMethod]
    public void Apply_CoverageRanksAboveTier()
    {
        var fullCoverage = Key(start: 0, coverage: 1, contiguity: 1, tier: MatchRank.TierFull);
        var lowCoverage = Key(start: 0, coverage: 0.5, contiguity: 1, tier: MatchRank.TierName);

        Assert.IsLessThan(lowCoverage, fullCoverage);
    }

    [TestMethod]
    public void Apply_NoNameFallback_CarriesPathCoverage()
    {
        var low = Key(MatchRank.NoNameFallback(0.2));
        var high = Key(MatchRank.NoNameFallback(0.4));

        Assert.IsLessThan(low, high);
    }

    [TestMethod]
    public void KeyLayout_ShapeOutranksLeftContiguous()
    {
        var leftWordStart = Key(start: 0, coverage: 0.5, contiguity: 1, shape: MatchRank.ShapeWordStart);
        var laterWholeWord = Key(start: 5, coverage: 1, contiguity: 1, shape: MatchRank.ShapeWholeWord);

        Assert.IsLessThan(leftWordStart, laterWholeWord);
    }

    [TestMethod]
    public void KeyLayout_LeftContiguousBreaksAShapeTie()
    {
        var leftWordStart = Key(start: 0, coverage: 0.5, contiguity: 1, shape: MatchRank.ShapeWordStart);
        var laterWordStart = Key(start: 5, coverage: 1, contiguity: 1, shape: MatchRank.ShapeWordStart);

        Assert.IsLessThan(laterWordStart, leftWordStart);
    }

    [TestMethod]
    public void KeyLayout_ShapeOutranksStartAndCoverage()
    {
        var wholeLater = Key(start: 5, coverage: 0.5, contiguity: 1, shape: MatchRank.ShapeWholeWord);
        var insideEarlier = Key(start: 0, coverage: 1, contiguity: 1, shape: MatchRank.ShapeInsideWord);

        Assert.IsLessThan(insideEarlier, wholeLater);
    }

    [TestMethod]
    public void KeyLayout_ShapeOrderIsWholeWordThenWordStartThenInsideThenScattered()
    {
        var whole = Key(start: 0, coverage: 1, contiguity: 1, shape: MatchRank.ShapeWholeWord);
        var wordStart = Key(start: 0, coverage: 1, contiguity: 1, shape: MatchRank.ShapeWordStart);
        var inside = Key(start: 0, coverage: 1, contiguity: 1, shape: MatchRank.ShapeInsideWord);
        var scattered = Key(start: 0, coverage: 1, contiguity: 1, shape: MatchRank.ShapeScattered);

        Assert.IsLessThan(wordStart, whole);
        Assert.IsLessThan(inside, wordStart);
        Assert.IsLessThan(scattered, inside);
    }

    [TestMethod]
    public void KeyLayout_ContiguityOutranksShape()
    {
        var wholeLoose = Key(start: 0, coverage: 1, contiguity: 0.5, shape: MatchRank.ShapeWholeWord);
        var scatteredTight = Key(start: 0, coverage: 1, contiguity: 1, shape: MatchRank.ShapeScattered);

        Assert.IsLessThan(wholeLoose, scatteredTight);
    }

    [TestMethod]
    public void KeyLayout_StartUsesAllSevenBits()
    {
        Assert.IsLessThan(Key(64, 1, 1), Key(63, 1, 1));
        Assert.IsLessThan(Key(100, 1, 1), Key(64, 1, 1));
        Assert.IsLessThan(Key(127, 1, 1), Key(126, 1, 1));
        Assert.AreEqual(Key(127, 1, 1), Key(200, 1, 1), "past StartCap the packed key saturates, it does not overflow");
    }

    [TestMethod]
    public void KeyLayout_ContiguityOutranksStart()
    {
        var contiguousLater = Key(start: 5, coverage: 0.5, contiguity: 1);
        var scatteredAtStart = Key(start: 0, coverage: 1, contiguity: 0.5);

        Assert.IsLessThan(scatteredAtStart, contiguousLater);
    }

    [TestMethod]
    public void KeyLayout_StartOutranksCoverage()
    {
        var earlyLowCoverage = Key(start: 1, coverage: 0.2, contiguity: 1);
        var laterHighCoverage = Key(start: 5, coverage: 1, contiguity: 1);

        Assert.IsLessThan(laterHighCoverage, earlyLowCoverage);
    }

    [TestMethod]
    public void KeyLayout_TierBreaksTies()
    {
        var literal = Key(start: 0, coverage: 0.5, contiguity: 1, tier: MatchRank.TierName);
        var initials = Key(start: 0, coverage: 0.5, contiguity: 1, tier: MatchRank.TierInitials);
        var full = Key(start: 0, coverage: 0.5, contiguity: 1, tier: MatchRank.TierFull);

        Assert.IsLessThan(initials, literal);
        Assert.IsLessThan(full, initials);
    }

    [TestMethod]
    public void Compare_FilesBeforeFolders_WhenTheMatchIsOtherwiseEqual()
    {
        var file = new SearchResult { Name = "atlas", Path = @"C:\data\atlas" };
        var folder = new SearchResult { Name = "atlas", Path = @"C:\data\atlas", IsDir = true };
        var pattern = FzfPattern.Parse("atlas");
        SearchResultRelevance.Apply(file, pattern);
        SearchResultRelevance.Apply(folder, pattern);

        Assert.IsLessThan(0, SearchResultRankComparer.Instance.Compare(file, folder));
    }

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

    // The full window reads the two strong/weak signals back out of the packed key instead of rebuilding a
    // MatchRank, so the bit layout and MatchRank.IsStrong have to stay in step -- a drift here would give
    // one query two different strong/weak boundaries, and with them two different scores.
    [TestMethod]
    public void IsStrongMatch_AgreesWithMatchRankIsStrong()
    {
        var tiers = new[] { MatchRank.TierName, MatchRank.TierInitials, MatchRank.TierFull, MatchRank.TierFullRare };
        var shapes = new[]
        {
            MatchRank.ShapeWholeWord,
            MatchRank.ShapeWordStart,
            MatchRank.ShapeInsideWord,
            MatchRank.ShapeScattered,
            MatchRank.ShapeNoName,
        };

        foreach (var tier in tiers)
        {
            foreach (var shape in shapes)
            {
                // A non-zero low word keeps the key distinguishable from "never packed" (see Apply below).
                var rank = new MatchRank(tier, 0, 1d, 1d, shape);
                var result = new SearchResult { Name = "atlas", Path = @"C:\atlas" };
                result.RankSortKey = (ulong)Key(rank) << 32 | 0xFFFF_FFFFUL;

                Assert.AreEqual(
                    rank.IsStrong,
                    SearchResultRelevance.IsStrongMatch(result.RankSortKey),
                    $"tier {tier} shape {shape} disagree");
            }
        }

        Assert.IsFalse(SearchResultRelevance.IsStrongMatch((ulong)Key(new MatchRank(MatchRank.TierFullRare, 0, 1d, 1d, MatchRank.ShapeWholeWord)) << 32));
        Assert.IsFalse(SearchResultRelevance.IsStrongMatch((ulong)Key(new MatchRank(MatchRank.TierName, 0, 1d, 1d, MatchRank.ShapeScattered)) << 32));
    }

    private static uint Key(MatchRank rank) => SearchResultRelevance.HighKey(rank);

    private static uint Key(int start, double coverage, double contiguity, int tier = 0, int shape = MatchRank.ShapeWholeWord) =>
        SearchResultRelevance.HighKey(new MatchRank(tier, start, coverage, contiguity, shape));

    private static ulong Ranked(string path, string name, string query)
    {
        var result = new SearchResult { Name = name, Path = path };
        SearchResultRelevance.Apply(result, FzfPattern.Parse(query));
        return result.RankSortKey;
    }
}
