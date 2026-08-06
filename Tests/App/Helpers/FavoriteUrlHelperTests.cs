using Lertaro.App.Helpers;

namespace Lertaro.App.Tests.Helpers;

[TestClass]
public sealed class FavoriteUrlHelperTests
{
    [TestMethod]
    public void IsWebUrl_HttpUrl_ReturnsTrue() => Assert.IsTrue(FavoriteUrlHelper.IsWebUrl("http://example.com"));

    [TestMethod]
    public void IsWebUrl_HttpsUrl_ReturnsTrue() => Assert.IsTrue(FavoriteUrlHelper.IsWebUrl("https://example.com"));

    [TestMethod]
    public void IsWebUrl_UrlWithSurroundingWhitespace_ReturnsTrue() => Assert.IsTrue(FavoriteUrlHelper.IsWebUrl("  https://example.com  "));

    [TestMethod]
    public void IsWebUrl_LocalFilePath_ReturnsFalse() => Assert.IsFalse(FavoriteUrlHelper.IsWebUrl(@"C:\folder\file.txt"));

    [TestMethod]
    public void IsWebUrl_FileScheme_ReturnsFalse() => Assert.IsFalse(FavoriteUrlHelper.IsWebUrl("file:///C:/folder"));

    [TestMethod]
    public void IsWebUrl_NullOrWhitespace_ReturnsFalse()
    {
        Assert.IsFalse(FavoriteUrlHelper.IsWebUrl(null));
        Assert.IsFalse(FavoriteUrlHelper.IsWebUrl("   "));
    }

    [TestMethod]
    public void IsWebUrl_RelativePath_ReturnsFalse() => Assert.IsFalse(FavoriteUrlHelper.IsWebUrl("relative\\path"));
}
