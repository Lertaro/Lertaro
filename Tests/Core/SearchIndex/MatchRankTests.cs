using Lertaro.Core.SearchIndex;

namespace Lertaro.Core.Tests.SearchIndex;

// The one rank a row gets when its own NAME did not match the query at all: it was reached through a
// transliteration of that name, or by an ancestor folder completing the query (see
// SearchResultRelevance.Apply). Both windows order those rows by it, so the value is shared rather than
// derived twice -- the quick window used to leave them at NoMatch and fall back to path length, which put
// the same rows in a different order in the two windows for one query.
[TestClass]
public sealed class MatchRankTests
{
    [TestMethod]
    public void NoNameFallback_RanksByPathCoverageWithThePackedKeysStartCap()
    {
        var rank = MatchRank.NoNameFallback(0.42);

        Assert.AreEqual(MatchRank.ShapeNoName, rank.Shape, "the row's own name is what did not match");
        Assert.AreEqual(MatchRank.TierFull, rank.Tier, "reached through a transliteration, the weakest tier");
        Assert.AreEqual(MatchRank.StartCap, rank.Start, "past this the full window's packed key cannot tell two starts apart");
        Assert.AreEqual(0.42, rank.Weight);
    }

    // Deliberately a rank and not a NoMatch: its Start is the cap rather than "did not match", which is what
    // lets it carry a weight for the ladders to order those rows by, while NoName -- the worst shape there is
    // -- keeps them below every genuine match.
    [TestMethod]
    public void NoNameFallback_IsARankRatherThanNoMatch()
    {
        Assert.IsTrue(MatchRank.NoNameFallback(0).IsMatch);
        Assert.IsFalse(MatchRank.NoMatch.IsMatch);
        Assert.IsGreaterThan(MatchRank.ShapeScattered, MatchRank.ShapeNoName);
    }
}
