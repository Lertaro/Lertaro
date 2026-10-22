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
        var contiguous = new MatchRank(MatchRank.TierName, 0, 0.20, Contiguity: 1.0, Shape: MatchRank.ShapeScattered);
        var scattered = new MatchRank(MatchRank.TierName, 0, 0.30, Contiguity: 0.2, Shape: MatchRank.ShapeScattered);

        Assert.IsTrue(
            SearchResultRelevance.HighKey(contiguous) < SearchResultRelevance.HighKey(scattered),
            "A contiguous hit must outrank a scattered one that only covers more of its name.");
    }

    [TestMethod]
    public void LeftmostHitBeatsAContiguousHitFurtherRight()
    {
        // The right-hand row is a whole word with a perfect run, but it starts later in the name.
        var leftmost = new MatchRank(MatchRank.TierName, 0, 0.10, Contiguity: 0.3, Shape: MatchRank.ShapeScattered);
        var furtherRight = new MatchRank(MatchRank.TierName, 5, 0.20, Contiguity: 1.0, Shape: MatchRank.ShapeWholeWord);

        Assert.IsTrue(
            SearchResultRelevance.HighKey(leftmost) < SearchResultRelevance.HighKey(furtherRight),
            "A hit that starts further left must outrank one further right, whatever its shape or contiguity.");
    }
}
