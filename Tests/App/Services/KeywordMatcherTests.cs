using Lertaro.App.Services;

namespace Lertaro.App.Tests.Services;

[TestClass]
public sealed class KeywordMatcherTests
{
    private static readonly string[] Keywords = ["g", "yt", "calc "];

    [TestMethod]
    public void TryMatchKeyword_ExactKeywordNoArgument_ReturnsEmptyArgument()
    {
        var match = KeywordMatcher.TryMatchKeyword("g", Keywords);

        Assert.IsNotNull(match);
        Assert.AreEqual("g", match.Value.Keyword);
        Assert.AreEqual("", match.Value.ArgumentText);
    }

    [TestMethod]
    public void TryMatchKeyword_KeywordWithTrailingArgument_SplitsKeywordAndArgument()
    {
        var match = KeywordMatcher.TryMatchKeyword("g hello world", Keywords);

        Assert.IsNotNull(match);
        Assert.AreEqual("g", match.Value.Keyword);
        Assert.AreEqual("hello world", match.Value.ArgumentText);
    }

    [TestMethod]
    public void TryMatchKeyword_ExtraLeadingSpaceInArgument_IsTrimmed()
    {
        var match = KeywordMatcher.TryMatchKeyword("g   hello", Keywords);

        Assert.IsNotNull(match);
        Assert.AreEqual("hello", match.Value.ArgumentText);
    }

    [TestMethod]
    public void TryMatchKeyword_KeywordItselfHasTrailingSpace_IsTrimmedBeforeMatching()
    {
        var match = KeywordMatcher.TryMatchKeyword("calc 2+2", Keywords);

        Assert.IsNotNull(match);
        Assert.AreEqual("calc", match.Value.Keyword);
        Assert.AreEqual("2+2", match.Value.ArgumentText);
    }

    [TestMethod]
    public void TryMatchKeyword_MatchIsCaseInsensitive()
    {
        var match = KeywordMatcher.TryMatchKeyword("G hello", Keywords);

        Assert.IsNotNull(match);
        Assert.AreEqual("g", match.Value.Keyword);
    }

    [TestMethod]
    public void TryMatchKeyword_PartialPrefixNoArgumentSeparator_MatchesForAutocomplete()
    {
        // No space in the query at all -> partial-prefix branch: "y" could still become "yt".
        var match = KeywordMatcher.TryMatchKeyword("y", Keywords);

        Assert.IsNotNull(match);
        Assert.AreEqual("yt", match.Value.Keyword);
        Assert.AreEqual("", match.Value.ArgumentText);
    }

    [TestMethod]
    public void TryMatchKeyword_PartialPrefixWithArgumentSeparator_DoesNotMatch()
    {
        // Once the query has a space, partial-prefix autocomplete no longer applies.
        var match = KeywordMatcher.TryMatchKeyword("y something", Keywords);

        Assert.IsNull(match);
    }

    [TestMethod]
    public void TryMatchKeyword_NoMatch_ReturnsNull() =>
        Assert.IsNull(KeywordMatcher.TryMatchKeyword("zzz not a keyword", Keywords));

    [TestMethod]
    public void TryMatchKeyword_EmptyKeywordList_ReturnsNull() =>
        Assert.IsNull(KeywordMatcher.TryMatchKeyword("g", Array.Empty<string>()));

    [TestMethod]
    public void TryMatchKeyword_BlankKeywordsInList_AreSkipped()
    {
        var match = KeywordMatcher.TryMatchKeyword("g test", new[] { "", "   ", "g" });

        Assert.IsNotNull(match);
        Assert.AreEqual("g", match.Value.Keyword);
    }

    [TestMethod]
    public void TryMatchKeyword_QueryWithSurroundingWhitespace_IsTrimmedBeforeMatching()
    {
        var match = KeywordMatcher.TryMatchKeyword("  g hello  ", Keywords);

        Assert.IsNotNull(match);
        Assert.AreEqual("hello", match.Value.ArgumentText);
    }

    [TestMethod]
    public void TryMatchKeyword_FirstMatchingKeywordWins()
    {
        // "g" matches exactly before "yt"/"calc" are even considered.
        var match = KeywordMatcher.TryMatchKeyword("g", new[] { "g", "g" });

        Assert.IsNotNull(match);
        Assert.AreEqual("g", match.Value.Keyword);
    }
}
