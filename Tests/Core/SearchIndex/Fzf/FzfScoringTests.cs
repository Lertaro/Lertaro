using Lertaro.Core.SearchIndex.Fzf;

namespace Lertaro.Core.Tests.SearchIndex.Fzf;

[TestClass]
public sealed class FzfScoringTests
{
    [TestMethod]
    public void FindFuzzyScope_NoMatch_ReturnsFalse()
    {
        var found = FzfScoring.FindFuzzyScope("hello", "xyz", caseSensitive: true, out _, out _);

        Assert.IsFalse(found);
    }

    [TestMethod]
    public void FindFuzzyScope_SubsequenceMatch_ReturnsTightestBoundingRange()
    {
        var found = FzfScoring.FindFuzzyScope("hello world", "wor", caseSensitive: true, out var start, out var end);

        Assert.IsTrue(found);
        Assert.AreEqual(5, start);
        Assert.AreEqual(9, end);
    }

    [TestMethod]
    public void FindFuzzyScope_CaseInsensitive_MatchesRegardlessOfCase()
    {
        var found = FzfScoring.FindFuzzyScope("Hello World", "WOR", caseSensitive: false, out var start, out var end);

        Assert.IsTrue(found);
        Assert.AreEqual(5, start);
        Assert.AreEqual(9, end);
    }

    [TestMethod]
    public void FindFuzzyScope_CaseSensitiveWrongCase_NoMatch()
    {
        var found = FzfScoring.FindFuzzyScope("Hello World", "WOR", caseSensitive: true, out _, out _);

        Assert.IsFalse(found);
    }

    [TestMethod]
    public void CalculateScore_PatternFullyMatchedWithinRange_ReturnsNonNegativeScore()
    {
        var score = FzfScoring.CalculateScore("abc", "abc", 0, 3, caseSensitive: true, FzfScoringScheme.Default);

        Assert.IsGreaterThanOrEqualTo(0, score);
    }

    [TestMethod]
    public void CalculateScore_PatternNotFullyMatchedWithinRange_ReturnsNegativeOne()
    {
        var score = FzfScoring.CalculateScore("abc", "abd", 0, 3, caseSensitive: true, FzfScoringScheme.Default);

        Assert.AreEqual(-1, score);
    }

    [TestMethod]
    public void CalculateScore_ConsecutiveMatch_ScoresHigherThanScatteredMatch()
    {
        var consecutive = FzfScoring.CalculateScore("abcxyz", "abc", 0, 6, caseSensitive: true, FzfScoringScheme.Default);
        var scattered = FzfScoring.CalculateScore("a-b-cxyz", "abc", 0, 8, caseSensitive: true, FzfScoringScheme.Default);

        Assert.IsGreaterThan(scattered, consecutive);
    }

    [TestMethod]
    public void CalculateScore_EmptyPattern_MatchesImmediatelyWithNoPositiveScore()
    {
        var score = FzfScoring.CalculateScore("abc", "", 0, 0, caseSensitive: true, FzfScoringScheme.Default);

        Assert.AreEqual(0, score);
    }
}
