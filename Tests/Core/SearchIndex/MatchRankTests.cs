using Lertaro.Core.SearchIndex;

namespace Lertaro.Core.Tests.SearchIndex;

// MatchRank is the single quality contract both windows rank by. NoNameFallback is the one rank a row
// gets when its own NAME did not match at all (an ancestor folder completed the query): it must stay a
// rank rather than NoMatch so it can carry path coverage, and its coverage must actually participate.
[TestClass]
public sealed class MatchRankTests
{
    [TestMethod]
    public void NoNameFallback_CarriesPathCoverageAndTheStartCap()
    {
        var rank = MatchRank.NoNameFallback(0.42);

        Assert.AreEqual(MatchRank.TierFull, rank.Tier, "reached through a transliteration, the weakest tier");
        Assert.AreEqual(MatchRank.StartCap, rank.Start, "past this the packed key saturates");
        Assert.AreEqual(0.42, rank.Coverage);
        Assert.AreEqual(0.0, rank.Contiguity, "a path is not a contiguous name match");
        Assert.AreEqual(0.0, rank.Weight);
    }

    [TestMethod]
    public void NoNameFallback_IsARankRatherThanNoMatch()
    {
        Assert.IsTrue(MatchRank.NoNameFallback(0).IsMatch);
        Assert.IsFalse(MatchRank.NoMatch.IsMatch);
    }

    [TestMethod]
    public void CompareQuality_LeftContiguousThenContiguityThenStartThenCoverageThenTier()
    {
        var leftContiguous = new MatchRank(MatchRank.TierName, 0, 0.5, 1.0);
        var contiguousLater = new MatchRank(MatchRank.TierName, 5, 0.5, 1.0);
        var scatteredEarlier = new MatchRank(MatchRank.TierName, 0, 1.0, 0.5);
        var lowerCoverage = new MatchRank(MatchRank.TierName, 0, 0.4, 1.0);
        var worseTier = new MatchRank(MatchRank.TierFull, 0, 0.5, 1.0);

        Assert.IsLessThan(0, MatchRank.CompareQuality(leftContiguous, contiguousLater));
        Assert.IsLessThan(0, MatchRank.CompareQuality(contiguousLater, scatteredEarlier));
        Assert.IsLessThan(0, MatchRank.CompareQuality(leftContiguous, lowerCoverage));
        Assert.IsLessThan(0, MatchRank.CompareQuality(leftContiguous, worseTier));
    }

    [TestMethod]
    public void CompareQuality_ShapeRanksWholeWordThenWordStartThenInsideThenScattered()
    {
        var whole = new MatchRank(MatchRank.TierName, 0, 1, 1, MatchRank.ShapeWholeWord);
        var start = new MatchRank(MatchRank.TierName, 0, 1, 1, MatchRank.ShapeWordStart);
        var inside = new MatchRank(MatchRank.TierName, 0, 1, 1, MatchRank.ShapeInsideWord);
        var scattered = new MatchRank(MatchRank.TierName, 0, 1, 1, MatchRank.ShapeScattered);

        Assert.IsLessThan(0, MatchRank.CompareQuality(whole, start));
        Assert.IsLessThan(0, MatchRank.CompareQuality(start, inside));
        Assert.IsLessThan(0, MatchRank.CompareQuality(inside, scattered));
    }

    [TestMethod]
    public void CompareQuality_ShapeOutranksStartAndCoverage()
    {
        var wholeLater = new MatchRank(MatchRank.TierName, 5, 0.5, 1, MatchRank.ShapeWholeWord);
        var insideEarlier = new MatchRank(MatchRank.TierName, 1, 1, 1, MatchRank.ShapeInsideWord);

        Assert.IsLessThan(0, MatchRank.CompareQuality(wholeLater, insideEarlier));
    }

    [TestMethod]
    public void NoPositiveTerms_IsARankButNotLeftContiguous()
    {
        var rank = MatchRank.NoPositiveTerms;

        Assert.IsTrue(rank.IsMatch);
        Assert.IsFalse(rank.IsLeftContiguous);
        Assert.AreEqual(0.0, rank.Coverage);
        Assert.AreEqual(0.0, rank.Contiguity);
    }
}
