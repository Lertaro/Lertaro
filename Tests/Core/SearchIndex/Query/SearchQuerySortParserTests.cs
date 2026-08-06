using Lertaro.Core.SearchIndex.Query;

namespace Lertaro.Core.Tests.SearchIndex.Query;

[TestClass]
public sealed class SearchQuerySortParserTests
{
    [TestMethod]
    public void Strip_NoSuffix_ReturnsQueryUnchangedWithEmptyTokens()
    {
        var result = SearchQuerySortParser.Strip("readme", out var tokens);

        Assert.AreEqual("readme", result);
        Assert.IsEmpty(tokens);
    }

    [TestMethod]
    public void Strip_ValidSuffix_ExtractsTokensAndTrimsQuery()
    {
        var result = SearchQuerySortParser.Strip("readme :a,b,c", out var tokens);

        Assert.AreEqual("readme", result);
        CollectionAssert.AreEqual(new[] { "a", "b", "c" }, tokens.ToArray());
    }

    [TestMethod]
    public void Strip_SuffixOnly_ReturnsEmptyQuery()
    {
        var result = SearchQuerySortParser.Strip(":a,b", out var tokens);

        Assert.AreEqual(string.Empty, result);
        CollectionAssert.AreEqual(new[] { "a", "b" }, tokens.ToArray());
    }

    [TestMethod]
    public void Strip_SuffixInMiddleOfQuery_IsNotTreatedAsSuffix()
    {
        var result = SearchQuerySortParser.Strip(":a,b readme", out var tokens);

        Assert.AreEqual(":a,b readme", result);
        Assert.IsEmpty(tokens);
    }

    [TestMethod]
    public void Strip_EmptyTokenInSuffix_IsRejectedAsNotASuffix()
    {
        var result = SearchQuerySortParser.Strip("readme :a,,c", out var tokens);

        Assert.AreEqual("readme :a,,c", result);
        Assert.IsEmpty(tokens);
    }

    [TestMethod]
    public void Strip_BareColon_IsRejectedAsTooShort()
    {
        var result = SearchQuerySortParser.Strip("readme :", out var tokens);

        Assert.AreEqual("readme :", result);
        Assert.IsEmpty(tokens);
    }

    [TestMethod]
    public void Strip_CustomPrefixChar_ExtractsTokensAndTrimsQuery()
    {
        var result = SearchQuerySortParser.Strip("readme #a,b,c", out var tokens, '#');

        Assert.AreEqual("readme", result);
        CollectionAssert.AreEqual(new[] { "a", "b", "c" }, tokens.ToArray());
    }

    [TestMethod]
    public void Strip_DoubleQuotedTokenWithSpaces_ExtractsTokenAndUnquotes()
    {
        var result = SearchQuerySortParser.Strip("file :\"hello world\"", out var tokens);

        Assert.AreEqual("file", result);
        CollectionAssert.AreEqual(new[] { "hello world" }, tokens.ToArray());
    }

    [TestMethod]
    public void Strip_SingleQuotedTokenWithSpaces_ExtractsTokenAndUnquotes()
    {
        var result = SearchQuerySortParser.Strip("file :'hello world'", out var tokens);

        Assert.AreEqual("file", result);
        CollectionAssert.AreEqual(new[] { "hello world" }, tokens.ToArray());
    }

    [TestMethod]
    public void Strip_WildcardSingleQuotedTokenWithSpaces_ExtractsTokenWithPrefix()
    {
        var result = SearchQuerySortParser.Strip("file ::'hello world'", out var tokens);

        Assert.AreEqual("file", result);
        CollectionAssert.AreEqual(new[] { ":hello world" }, tokens.ToArray());
    }

    [TestMethod]
    public void Strip_EscapedSpaceInUnquotedToken_ExtractsTokenAndUnescapesSpace()
    {
        var result = SearchQuerySortParser.Strip(@"file ::hello\ world", out var tokens);

        Assert.AreEqual("file", result);
        CollectionAssert.AreEqual(new[] { ":hello world" }, tokens.ToArray());
    }

    [TestMethod]
    public void Strip_EscapedQuotesInToken_UnescapesQuotesInResult()
    {
        var resultDouble = SearchQuerySortParser.Strip(@"file ::""hello \""world\""""", out var tokensDouble);

        Assert.AreEqual("file", resultDouble);
        CollectionAssert.AreEqual(new[] { ":hello \"world\"" }, tokensDouble.ToArray());

        var resultSingle = SearchQuerySortParser.Strip(@"file ::'hello \'world\''", out var tokensSingle);

        Assert.AreEqual("file", resultSingle);
        CollectionAssert.AreEqual(new[] { ":hello 'world'" }, tokensSingle.ToArray());
    }

    [TestMethod]
    public void Strip_EscapedBackslashInToken_UnescapesToSingleBackslash()
    {
        var result = SearchQuerySortParser.Strip(@"file ::""path\\folder""", out var tokens);

        Assert.AreEqual("file", result);
        CollectionAssert.AreEqual(new[] { @":path\folder" }, tokens.ToArray());
    }

    [TestMethod]
    public void Strip_MultipleQuotedTokensWithSpaces_ExtractsAllTokens()
    {
        var result = SearchQuerySortParser.Strip("file :'first token',\"second token\",simple", out var tokens);

        Assert.AreEqual("file", result);
        CollectionAssert.AreEqual(new[] { "first token", "second token", "simple" }, tokens.ToArray());
    }

    [TestMethod]
    public void Strip_PathOrTimeWithColon_IsIgnored()
    {
        var resultPath = SearchQuerySortParser.Strip(@"C:\path\file", out var tokensPath);
        Assert.AreEqual(@"C:\path\file", resultPath);
        Assert.IsEmpty(tokensPath);

        var resultTime = SearchQuerySortParser.Strip("12:30", out var tokensTime);
        Assert.AreEqual("12:30", resultTime);
        Assert.IsEmpty(tokensTime);
    }

    [TestMethod]
    public void StripExclusionBypass_LeadingAsterisk_IsStrippedAndFlagged()
    {
        var result = SearchQuerySortParser.StripExclusionBypass("*readme", out var bypass);

        Assert.AreEqual("readme", result);
        Assert.IsTrue(bypass);
    }

    [TestMethod]
    public void StripExclusionBypass_NoAsterisk_IsUnchanged()
    {
        var result = SearchQuerySortParser.StripExclusionBypass("readme", out var bypass);

        Assert.AreEqual("readme", result);
        Assert.IsFalse(bypass);
    }

    [TestMethod]
    public void StripExclusionBypass_EmptyQuery_DoesNotThrow()
    {
        var result = SearchQuerySortParser.StripExclusionBypass("", out var bypass);

        Assert.AreEqual("", result);
        Assert.IsFalse(bypass);
    }
}
