namespace Lertaro.Core.Tests.IndexV2.Search;

using Lertaro.Core.IndexV2.Search;
using Lertaro.Core.SearchIndex;

// The owner's ordering rule, pinned: leftmost match position, then how contiguous the hit is, then coverage,
// then how the match was found (shape/recency/depth do not take part). Both cases below were reported as
// "left side and contiguity are not honoured" -- a scattered hit on a short name used to win on coverage.
[TestClass]
public sealed class RankingRuleTests
{
    [TestMethod]
    public void ContiguousHitBeatsAScatteredOneAtTheSameStart()
    {
        // Same start; the scattered row covers more of its name but its letters are all over it.
        var contiguous = new MatchRank(MatchRank.TierName, 0, 0.20, MatchRank.ShapeScattered, Contiguity: 1.0);
        var scattered = new MatchRank(MatchRank.TierName, 0, 0.30, MatchRank.ShapeScattered, Contiguity: 0.2);

        Assert.IsTrue(
            SearchResultRelevance.HighKey(contiguous, false, 0, 0) < SearchResultRelevance.HighKey(scattered, false, 0, 0),
            "A contiguous hit must outrank a scattered one that only covers more of its name.");
    }

    [TestMethod]
    public void LeftmostHitBeatsAContiguousHitFurtherRight()
    {
        // The right-hand row is a whole word with a perfect run, but it starts later in the name.
        var leftmost = new MatchRank(MatchRank.TierName, 0, 0.10, MatchRank.ShapeScattered, Contiguity: 0.3);
        var furtherRight = new MatchRank(MatchRank.TierName, 5, 0.20, MatchRank.ShapeWholeWord, Contiguity: 1.0);

        Assert.IsTrue(
            SearchResultRelevance.HighKey(leftmost, false, 0, 0) < SearchResultRelevance.HighKey(furtherRight, false, 0, 0),
            "A hit that starts further left must outrank one further right, whatever its shape or contiguity.");
    }
}
