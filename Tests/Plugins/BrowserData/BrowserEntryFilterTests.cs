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
}
