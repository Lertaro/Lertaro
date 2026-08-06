using Lertaro.Core.SearchIndex.Fzf;

namespace Lertaro.Core.Tests.SearchIndex.Fzf;

[TestClass]
public sealed class FzfFuzzyMatcherTests
{
    [TestMethod]
    public void FuzzyMatchV2_EmptyPattern_MatchesAtStartWithZeroScore()
    {
        var result = FzfFuzzyMatcher.FuzzyMatchV2("anything.txt", "", caseSensitive: false, FzfScoringScheme.Default);

        Assert.AreEqual(0, result.Start);
        Assert.AreEqual(0, result.End);
        Assert.AreEqual(0, result.Score);
    }

    [TestMethod]
    public void FuzzyMatchV2_PatternLongerThanText_NoMatch()
    {
        var result = FzfFuzzyMatcher.FuzzyMatchV2("abc", "abcdef", caseSensitive: false, FzfScoringScheme.Default);

        Assert.AreEqual(-1, result.Start);
        Assert.AreEqual(-1, result.End);
    }

    [TestMethod]
    public void FuzzyMatchV2_ContiguousSubstring_Matches()
    {
        var result = FzfFuzzyMatcher.FuzzyMatchV2("readme.md", "read", caseSensitive: false, FzfScoringScheme.Default);

        Assert.AreEqual(0, result.Start);
        Assert.AreEqual(4, result.End);
        Assert.IsGreaterThan(0, result.Score);
    }

    [TestMethod]
    public void FuzzyMatchV2_OutOfOrderCharacters_DoNotMatch()
    {
        // "readme.md" contains r, e, a, d in that scanning order but not "d" before "r" -- a pattern
        // requiring the letters in a different relative order than they appear should not match.
        var result = FzfFuzzyMatcher.FuzzyMatchV2("read", "dare", caseSensitive: false, FzfScoringScheme.Default);

        Assert.AreEqual(-1, result.Start);
    }

    [TestMethod]
    public void FuzzyMatchV2_ScatteredCharactersInOrder_StillMatch()
    {
        // "lertaro" contains l, r, o as a scattered subsequence at indices 0, 2, 6.
        var result = FzfFuzzyMatcher.FuzzyMatchV2("lertaro", "lro", caseSensitive: false, FzfScoringScheme.Default);

        Assert.AreEqual(0, result.Start);
        Assert.AreEqual(7, result.End);
    }

    [TestMethod]
    public void FuzzyMatchV2_CaseInsensitiveByDefault_MatchesDifferentCase()
    {
        var result = FzfFuzzyMatcher.FuzzyMatchV2("README.md", "read", caseSensitive: false, FzfScoringScheme.Default);

        Assert.AreNotEqual(-1, result.Start);
    }

    [TestMethod]
    public void FuzzyMatchV2_CaseSensitive_RejectsDifferentCase()
    {
        var result = FzfFuzzyMatcher.FuzzyMatchV2("README.md", "read", caseSensitive: true, FzfScoringScheme.Default);

        Assert.AreEqual(-1, result.Start);
    }

    [TestMethod]
    public void FuzzyMatchV2_ConsecutiveMatch_ScoresHigherThanScattered()
    {
        var consecutive = FzfFuzzyMatcher.FuzzyMatchV2("abcdef", "abc", caseSensitive: false, FzfScoringScheme.Default);
        var scattered = FzfFuzzyMatcher.FuzzyMatchV2("axbxcx", "abc", caseSensitive: false, FzfScoringScheme.Default);

        Assert.IsGreaterThan(scattered.Score, consecutive.Score);
    }
}
