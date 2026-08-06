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

    [TestMethod]
    public void ComputeBestMatch_EmptyQuery_ReturnsNoMatch()
    {
        var (isMatch, weight) = FuzzyMatcher.ComputeBestMatch("", "readme");

        Assert.IsFalse(isMatch);
        Assert.AreEqual(0, weight);
    }

    [TestMethod]
    public void ComputeBestMatch_PrimaryTextMatches_ReturnsMatch()
    {
        var (isMatch, weight) = FuzzyMatcher.ComputeBestMatch("read", "readme.md");

        Assert.IsTrue(isMatch);
        Assert.IsGreaterThan(0, weight);
    }

    [TestMethod]
    public void ComputeBestMatch_OnlyAlternateTextMatches_ReturnsMatch()
    {
        var (isMatch, _) = FuzzyMatcher.ComputeBestMatch("read", "notes.txt", new[] { "readme.md" });

        Assert.IsTrue(isMatch);
    }

    [TestMethod]
    public void ComputeBestMatch_NeitherPrimaryNorAlternateMatches_ReturnsNoMatch()
    {
        var (isMatch, weight) = FuzzyMatcher.ComputeBestMatch("xyz", "notes.txt", new[] { "readme.md" });

        Assert.IsFalse(isMatch);
        Assert.AreEqual(0, weight);
    }

    [TestMethod]
    public void ComputeBestMatch_TakesHighestWeightAcrossAllTexts()
    {
        // Alternate text 2 ("read.txt") is a tighter, higher-weight match for "read" than the noisier
        // primary text -- ComputeBestMatch must take the max weight, not just the first match found.
        var (isMatch, weight) = FuzzyMatcher.ComputeBestMatch(
            "read", "r_e_a_d_noisy", new[] { "unrelated", "read.txt" });

        Assert.IsTrue(isMatch);
        Assert.AreEqual(FuzzyMatcher.ComputeMatchWeight("read.txt", "read"), weight);
    }
}
