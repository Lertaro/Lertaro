using Lertaro.Core.SearchIndex;

namespace Lertaro.Core.Tests.SearchIndex;

// "A precise query must line up with syllable boundaries."
//
// Reported: "ex" reached 学习 (xue + xi) and 人行道 (ren + xing) although the user meant the initials of
// two characters. The query is a valid syllable sequence, so it was split into "e" + "x"; with no boundary
// rule "e" matched the TAIL of one syllable and "x" the HEAD of the next, splicing letters that never
// belong to the same sound. 恶性/恶心 were correct -- they come from the initials alias "ex".
//
// Pure rule only: no alias provider is registered here, because Core's tests deliberately run without one
// (several assert behaviour that depends on there being none). The end-to-end pinyin behaviour lives in
// the App test project, alongside the other alias tests.
[TestClass]
public sealed class AliasMatchRulesTests
{
    // A separator no real text contains, standing in for the pinyin provider's own.
    private const char Sep = (char)2;

    [TestMethod]
    public void Alignment_AllowsTheStartOfTheAlias()
    {
        Assert.IsTrue(AliasMatchRules.IsBoundaryAligned(Sep, $"zheng{Sep}shu", 0));
    }

    [TestMethod]
    public void Alignment_AllowsAPositionRightAfterASeparator()
    {
        Assert.IsTrue(AliasMatchRules.IsBoundaryAligned(Sep, $"zheng{Sep}shu", 6));
    }

    [TestMethod]
    public void Alignment_RejectsAMidSyllableStart()
    {
        // Index 3 is inside "zheng" -- matching there is what spliced xue+xi into "ex".
        Assert.IsFalse(AliasMatchRules.IsBoundaryAligned(Sep, $"zheng{Sep}shu", 3));
    }

    [TestMethod]
    public void Alignment_RejectsAMidSyllableStartInTheLastSyllable()
    {
        // Only the START of a match is constrained; a match may still run to the end (that is what keeps a
        // half-typed trailing syllable working), but it may not START inside a syllable.
        Assert.IsFalse(AliasMatchRules.IsBoundaryAligned(Sep, $"zheng{Sep}shu", 8));
    }

    [TestMethod]
    public void Alignment_FlatAliasIsAlignedEverywhere()
    {
        // An alias with no separator is the per-character initials shape, where every position is a real
        // boundary -- "x" must be allowed to match the second character of "ex".
        Assert.IsTrue(AliasMatchRules.IsBoundaryAligned(Sep, "ex", 1));
    }

    [TestMethod]
    public void Alignment_PolyphonicSegmentBoundaryCounts()
    {
        // A '|'-joined reading opens a fresh segment, so a match right after that character is aligned.
        Assert.IsTrue(AliasMatchRules.IsBoundaryAligned(Sep, $"zhong{Sep}guo|zhong{Sep}hua|zhong", 10));
    }

    [TestMethod]
    public void Alignment_ProviderWithoutASeparatorIsNeverConstrained()
    {
        Assert.IsTrue(AliasMatchRules.IsBoundaryAligned('\0', "anything", 3));
    }

    [TestMethod]
    public void Alignment_Utf8TwinAgreesWithTheCharVersion()
    {
        var alias = $"zheng{Sep}shu";
        var bytes = System.Text.Encoding.UTF8.GetBytes(alias);

        for (var i = 0; i <= bytes.Length; i++)
            Assert.AreEqual(
                AliasMatchRules.IsBoundaryAligned(Sep, alias, i),
                AliasMatchRules.IsBoundaryAligned(Sep, bytes, i),
                $"position {i}");
    }

    [TestMethod]
    public void AllowsMatch_PreciseQueryAppliesTheRule()
    {
        Assert.IsFalse(AliasMatchRules.AllowsMatch(precise: true, Sep, $"zheng{Sep}shu", 3));
        Assert.IsTrue(AliasMatchRules.AllowsMatch(precise: true, Sep, $"zheng{Sep}shu", 6));
    }

    [TestMethod]
    public void AllowsMatch_FuzzyTermIsExempt()
    {
        // A fuzzy term asked for a loose match, so the operator the user typed would be contradicted by
        // applying the boundary rule.
        Assert.IsTrue(AliasMatchRules.AllowsMatch(precise: false, Sep, $"zheng{Sep}shu", 3));
    }

    [TestMethod]
    public void Tier_LiteralNameBeatsEveryAliasShape()
    {
        Assert.AreEqual(MatchRank.TierName, AliasMatchRules.TierFor(Sep, matchedName: true, $"zheng{Sep}shu"));
    }

    [TestMethod]
    public void Tier_InitialsBeatFullReading()
    {
        // The full reading is exactly the alias carrying the separator; the initials alias carries none.
        Assert.AreEqual(MatchRank.TierInitials, AliasMatchRules.TierFor(Sep, matchedName: false, "zs"));
        Assert.AreEqual(MatchRank.TierFull, AliasMatchRules.TierFor(Sep, matchedName: false, $"zheng{Sep}shu"));
    }

    [TestMethod]
    public void Tier_ProviderWithoutASeparatorIsScoredAsInitials()
    {
        // Nothing distinguishes its two shapes, so it cannot be ranked as a full reading.
        Assert.AreEqual(MatchRank.TierInitials, AliasMatchRules.TierFor('\0', matchedName: false, "whatever"));
    }

    [TestMethod]
    public void Tier_LiteralBeatsInitialsBeatsFull()
    {
        Assert.IsLessThan(MatchRank.TierInitials, MatchRank.TierName);
        Assert.IsLessThan(MatchRank.TierFull, MatchRank.TierInitials);
    }
}
