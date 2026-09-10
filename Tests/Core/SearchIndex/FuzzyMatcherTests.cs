using Lertaro.Core.SearchIndex;

namespace Lertaro.Core.Tests.SearchIndex;

// Sticks to plain ASCII text throughout: FuzzyMatcher.IsMatch only reaches AliasProviderRegistry's
// process-wide provider list once the candidate text is non-ASCII (see HasNonAscii's gate), so ASCII
// inputs exercise the direct-match path without depending on (or disturbing) whatever alias providers
// are or aren't registered elsewhere in the process.
[TestClass]
public sealed class FuzzyMatcherTests
{
    [TestMethod]
    public void IsMatch_SubsequenceMatch_ReturnsTrue() => Assert.IsTrue(FuzzyMatcher.IsMatch("rdm", "readme.md"));

    // Case is ignored in both directions: the query's own case never changes what matches, and the
    // candidate's case never does either. This replaces fzf's smart-case rule, under which a capital
    // anywhere in the query made it case-sensitive and "WX" stopped matching "wxfef.doc".
    [TestMethod]
    public void IsMatch_UpperCaseQuery_MatchesLowerCaseText() => Assert.IsTrue(FuzzyMatcher.IsMatch("WX", "wxfef.doc"));

    [TestMethod]
    public void IsMatch_LowerCaseQuery_MatchesUpperCaseText() => Assert.IsTrue(FuzzyMatcher.IsMatch("wx", "WXFEF.DOC"));

    [TestMethod]
    public void ComputeMatchWeight_UpperCaseQuery_MatchesLowerCaseText() =>
        Assert.AreEqual(FuzzyMatcher.ComputeMatchWeight("wxfef.doc", "wx"), FuzzyMatcher.ComputeMatchWeight("wxfef.doc", "WX"));

    [TestMethod]
    public void IsMatch_NoSubsequence_ReturnsFalse() => Assert.IsFalse(FuzzyMatcher.IsMatch("xyz", "readme.md"));

    [TestMethod]
    public void IsMatch_EmptyPattern_ReturnsFalse() => Assert.IsFalse(FuzzyMatcher.IsMatch("", "readme.md"));

    [TestMethod]
    public void IsMatch_EmptyText_ReturnsFalse() => Assert.IsFalse(FuzzyMatcher.IsMatch("readme", ""));

    [TestMethod]
    public void IsMatch_PureDriveSpecPattern_ReturnsFalse() =>
        // "d:\" parses down to zero real search terms (see the method's own comment) -- this seam has
        // no drive-scoped "list everything" mode, so it must not fall into "no terms = match anything".
        Assert.IsFalse(FuzzyMatcher.IsMatch(@"d:\", "readme.md"));

    [TestMethod]
    public void ComputeHighlightMask_EmptyText_ReturnsEmptyArray() => Assert.IsEmpty(FuzzyMatcher.ComputeHighlightMask("", "read"));

    [TestMethod]
    public void ComputeHighlightMask_EmptyQuery_ReturnsAllFalseMaskSizedToText()
    {
        var mask = FuzzyMatcher.ComputeHighlightMask("readme", "");

        Assert.HasCount(6, mask);
        Assert.IsFalse(Array.Exists(mask, m => m));
    }

    [TestMethod]
    public void ComputeHighlightMask_LiteralMatch_MarksMatchedCharacters()
    {
        var mask = FuzzyMatcher.ComputeHighlightMask("readme", "read");

        CollectionAssert.AreEqual(new[] { true, true, true, true, false, false }, mask);
    }

    [TestMethod]
    public void ComputeMatchWeight_EmptyInputs_ReturnsZero()
    {
        Assert.AreEqual(0, FuzzyMatcher.ComputeMatchWeight("", "read"));
        Assert.AreEqual(0, FuzzyMatcher.ComputeMatchWeight("readme", ""));
    }

    [TestMethod]
    public void ComputeMatchWeight_FullContiguousMatch_ReturnsOne() => Assert.AreEqual(1.0, FuzzyMatcher.ComputeMatchWeight("read", "read"));

    [TestMethod]
    public void ComputeMatchWeight_PartialScatteredMatch_ReturnsLessThanFullMatch()
    {
        var full = FuzzyMatcher.ComputeMatchWeight("read", "read");
        var partial = FuzzyMatcher.ComputeMatchWeight("r_e_a_d_me_long_tail", "read");

        Assert.IsLessThan(full, partial);
    }

    // Weight is pure coverage*contiguity -- position is a separate, higher tier (see ComputeMatchRank /
    // MatchRank_PositionOutranksWeight). This documents why they are separate: the shorter name keeps the
    // larger weight even though it matches later, so ranking by weight alone would invert the intended
    // left-side-first order.
    [TestMethod]
    public void ComputeMatchWeight_DoesNotFoldInPosition()
    {
        var atStart = FuzzyMatcher.ComputeMatchWeight("wxfef.doc", "wx");
        var oneIn = FuzzyMatcher.ComputeMatchWeight("iwxfe.mp", "wx");

        Assert.IsGreaterThan(atStart, oneIn);
    }

    [TestMethod]
    public void ComputeBestMatch_EmptyQuery_ReturnsNoMatch()
    {
        var match = FuzzyMatcher.ComputeBestMatch("", "readme");

        Assert.IsFalse(match.IsMatch);
        Assert.AreEqual(0, match.Weight);
    }

    [TestMethod]
    public void ComputeBestMatch_PrimaryTextMatches_ReturnsMatch()
    {
        var match = FuzzyMatcher.ComputeBestMatch("read", "readme.md");

        Assert.IsTrue(match.IsMatch);
        Assert.IsGreaterThan(0, match.Weight);
    }

    [TestMethod]
    public void ComputeBestMatch_OnlyAlternateTextMatches_ReturnsMatch()
    {
        var match = FuzzyMatcher.ComputeBestMatch("read", "notes.txt", new[] { "readme.md" });

        Assert.IsTrue(match.IsMatch);
    }

    [TestMethod]
    public void ComputeBestMatch_NeitherPrimaryNorAlternateMatches_ReturnsNoMatch()
    {
        var match = FuzzyMatcher.ComputeBestMatch("xyz", "notes.txt", new[] { "readme.md" });

        Assert.IsFalse(match.IsMatch);
        Assert.AreEqual(0, match.Weight);
    }

    [TestMethod]
    public void ComputeBestMatch_TakesHighestWeightAcrossAllTexts()
    {
        // Alternate text 2 ("read.txt") is a tighter, higher-weight match for "read" than the noisier
        // primary text -- ComputeBestMatch must take the max weight, not just the first match found.
        var match = FuzzyMatcher.ComputeBestMatch(
            "read", "r_e_a_d_noisy", new[] { "unrelated", "read.txt" });

        Assert.IsTrue(match.IsMatch);
        Assert.AreEqual(FuzzyMatcher.ComputeMatchWeight("read.txt", "read"), match.Weight);
    }

    // Start position is reported so the windows can rank "left-side match priority" above weight.
    [TestMethod]
    public void ComputeMatchRank_ReportsLeftmostMatchedIndex()
    {
        Assert.AreEqual(0, FuzzyMatcher.ComputeMatchRank("wxfef.doc", "wx").Start);
        Assert.AreEqual(1, FuzzyMatcher.ComputeMatchRank("iwxfe.mp", "wx").Start);
    }

    [TestMethod]
    public void ComputeMatchRank_NoMatch_HasSentinelStartAndIsNotAMatch()
    {
        var rank = FuzzyMatcher.ComputeMatchRank("readme.md", "xyz");

        Assert.IsFalse(rank.IsMatch);
        Assert.AreEqual(0, rank.Weight);
    }

    // The same shorter-name case that defeated a multiplicative position factor: start position is
    // compared on its own, so "wxfef.doc" (start 0) beats "iwxfe.mp" (start 1) even though the shorter
    // name has the larger coverage share.
    [TestMethod]
    public void MatchRank_PositionOutranksWeight()
    {
        var leftmost = FuzzyMatcher.ComputeMatchRank("wxfef.doc", "wx");
        var later = FuzzyMatcher.ComputeMatchRank("iwxfe.mp", "wx");

        Assert.IsGreaterThan(leftmost.Weight, later.Weight, "precondition: the shorter name has the larger weight");
        Assert.IsLessThan(later.Start, leftmost.Start, "the leftmost match must win on position");
    }
}
