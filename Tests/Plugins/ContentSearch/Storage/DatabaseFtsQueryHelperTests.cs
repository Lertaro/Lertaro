using Lertaro.Plugins.ContentSearch.Storage;

namespace Lertaro.Plugins.ContentSearch.Tests.Storage;

[TestClass]
public sealed class DatabaseFtsQueryHelperTests
{
    [TestMethod]
    public void BuildFtsQuery_SingleToken_FormatsTrigramQuery()
    {
        Assert.AreEqual("\"hello\"", DatabaseFtsQueryHelper.BuildFtsQuery("hello"));
        Assert.AreEqual("\"hi\"*", DatabaseFtsQueryHelper.BuildFtsQuery("hi"));
    }

    [TestMethod]
    public void BuildFtsQuery_MultipleTokens_CombinesWithAnd() => Assert.AreEqual("\"hello\" AND \"world\"", DatabaseFtsQueryHelper.BuildFtsQuery("hello world"));

    [TestMethod]
    public void BuildFtsQuery_EmptyOrWhitespace_ReturnsEmpty() => Assert.AreEqual(string.Empty, DatabaseFtsQueryHelper.BuildFtsQuery("   "));

    [TestMethod]
    public void BuildFtsQuery_SpecialQuotes_CleansCorrectly() => Assert.AreEqual("\"say\" AND \"hello\"", DatabaseFtsQueryHelper.BuildFtsQuery("say \"hello\""));
}
