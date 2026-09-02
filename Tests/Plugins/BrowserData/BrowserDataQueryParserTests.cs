namespace Lertaro.Plugins.BrowserData.Tests;

[TestClass]
public sealed class BrowserDataQueryParserTests
{
    [TestMethod]
    public void Parse_BookmarkTrigger_SelectsBookmarks()
    {
        var result = BrowserDataQueryParser.Parse("bb lertaro", "bb", "bh");

        Assert.AreEqual(BrowserDataSearchScope.Bookmarks, result.Scope);
        Assert.AreEqual("lertaro", result.SearchTerm);
    }

    [TestMethod]
    public void Parse_HistoryTrigger_SelectsHistory()
    {
        var result = BrowserDataQueryParser.Parse("bh lertaro", "bb", "bh");

        Assert.AreEqual(BrowserDataSearchScope.History, result.Scope);
        Assert.AreEqual("lertaro", result.SearchTerm);
    }

    [TestMethod]
    public void Parse_BareTrigger_LeavesSearchTermEmpty()
    {
        var result = BrowserDataQueryParser.Parse("BB", "bb", "bh");

        Assert.IsTrue(result.IsHandled);
        Assert.IsEmpty(result.SearchTerm);
    }

    [TestMethod]
    public void Parse_UnrelatedQuery_IsNotHandled()
    {
        var result = BrowserDataQueryParser.Parse("lertaro", "bb", "bh");

        Assert.IsFalse(result.IsHandled);
    }
}
