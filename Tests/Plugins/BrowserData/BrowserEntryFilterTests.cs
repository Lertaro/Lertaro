namespace Lertaro.Plugins.BrowserData.Tests;

[TestClass]
public sealed class BrowserEntryFilterTests
{
    [TestMethod]
    public void IsHttpUrl_HttpUrl_ReturnsTrue() => Assert.IsTrue(BrowserEntryFilter.IsHttpUrl("http://example.com"));

    [TestMethod]
    public void IsHttpUrl_HttpsUrl_ReturnsTrue() => Assert.IsTrue(BrowserEntryFilter.IsHttpUrl("https://example.com"));

    [TestMethod]
    public void IsHttpUrl_SchemeIsCaseInsensitive() => Assert.IsTrue(BrowserEntryFilter.IsHttpUrl("HTTPS://example.com"));

    [TestMethod]
    public void IsHttpUrl_ChromeExtensionUrl_ReturnsFalse() => Assert.IsFalse(BrowserEntryFilter.IsHttpUrl("chrome-extension://abc/page.html"));

    [TestMethod]
    public void IsHttpUrl_FileUrl_ReturnsFalse() => Assert.IsFalse(BrowserEntryFilter.IsHttpUrl("file:///C:/a.txt"));

    [TestMethod]
    public void IsHttpUrl_AboutUrl_ReturnsFalse() => Assert.IsFalse(BrowserEntryFilter.IsHttpUrl("about:blank"));

    [TestMethod]
    public void NormalizeBlacklist_TrimsDropsEmptyAndDeduplicatesIgnoringCase()
    {
        var rules = BrowserEntryFilter.NormalizeBlacklist([" example.com ", "", "EXAMPLE.COM", "   "]);

        Assert.HasCount(1, rules);
        Assert.AreEqual("example.com", rules[0]);
    }

    [TestMethod]
    public void IsBlacklisted_TitleContainsRuleIgnoringCase_ReturnsTrue()
    {
        var entry = new BrowserEntry("Example Dashboard", "https://other.test", isBookmark: true, sortKey: 0);

        Assert.IsTrue(BrowserEntryFilter.IsBlacklisted(entry, ["dashboard"]));
    }

    [TestMethod]
    public void IsBlacklisted_UrlContainsRule_ReturnsTrue()
    {
        var entry = new BrowserEntry("Home", "https://example.com/private", isBookmark: true, sortKey: 0);

        Assert.IsTrue(BrowserEntryFilter.IsBlacklisted(entry, ["EXAMPLE.COM"]));
    }

    [TestMethod]
    public void IsBlacklisted_DoesNotUseFuzzyMatching_ReturnsFalse()
    {
        var entry = new BrowserEntry("Example", "https://example.com", isBookmark: true, sortKey: 0);

        Assert.IsFalse(BrowserEntryFilter.IsBlacklisted(entry, ["exampel"]));
    }
}
