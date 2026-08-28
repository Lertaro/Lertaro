using Lertaro.Plugins.ContentSearch.Storage;

namespace Lertaro.Plugins.ContentSearch.Tests.Storage;

[TestClass]
public sealed class DatabaseFtsQueryHelperTests
{
    [TestMethod]
    public void BuildFtsQuery_SingleToken_FormattedForTrigram()
    {
        var result = DatabaseFtsQueryHelper.BuildFtsQuery("hello");
        Assert.AreEqual("\"hello\"", result);

        var shortResult = DatabaseFtsQueryHelper.BuildFtsQuery("hi");
        Assert.AreEqual("\"hi\"*", shortResult);
    }

    [TestMethod]
    public void BuildFtsQuery_MultipleTokens_CombinesWithAnd()
    {
        var result = DatabaseFtsQueryHelper.BuildFtsQuery("hello world");
        Assert.AreEqual("\"hello\" AND \"world\"", result);
    }

    [TestMethod]
    public void BuildFtsQuery_EmptyOrWhitespace_ReturnsEmpty()
    {
        var result = DatabaseFtsQueryHelper.BuildFtsQuery("   ");
        Assert.AreEqual(string.Empty, result);
    }

    [TestMethod]
    public void BuildFtsQuery_SpecialQuotes_CleansCorrectly()
    {
        var result = DatabaseFtsQueryHelper.BuildFtsQuery("say \"hello\"");
        Assert.AreEqual("\"say\" AND \"hello\"", result);
    }
}
