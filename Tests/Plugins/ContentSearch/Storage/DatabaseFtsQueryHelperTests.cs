using Lertaro.Plugins.ContentSearch.Storage;

namespace Lertaro.Plugins.ContentSearch.Tests.Storage;

[TestClass]
public sealed class DatabaseFtsQueryHelperTests
{
    [TestMethod]
    public void BuildFtsQuery_SingleToken_AppendsPrefixWildcard()
    {
        var result = DatabaseFtsQueryHelper.BuildFtsQuery("hello");
        Assert.AreEqual("\"hello\"*", result);
    }

    [TestMethod]
    public void BuildFtsQuery_MultipleTokens_CombinesWithAnd()
    {
        var result = DatabaseFtsQueryHelper.BuildFtsQuery("hello world");
        Assert.AreEqual("\"hello\"* AND \"world\"*", result);
    }

    [TestMethod]
    public void BuildFtsQuery_EmptyOrWhitespace_ReturnsEmpty()
    {
        var result = DatabaseFtsQueryHelper.BuildFtsQuery("   ");
        Assert.AreEqual(string.Empty, result);
    }

    [TestMethod]
    public void BuildFtsQuery_SpecialQuotes_EscapesQuotesCorrectly()
    {
        var result = DatabaseFtsQueryHelper.BuildFtsQuery("say \"hello\"");
        Assert.AreEqual("\"say\"* AND \"\"\"hello\"\"\"*", result);
    }
}
