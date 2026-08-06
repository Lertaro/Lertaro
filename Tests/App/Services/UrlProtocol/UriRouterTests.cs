using Lertaro.App.Services.UrlProtocol;

namespace Lertaro.App.Tests.Services.UrlProtocol;

[TestClass]
public sealed class UriRouterTests
{
    [TestMethod]
    public void IsLertaroUri_LertaroScheme_ReturnsTrue() => Assert.IsTrue(UriRouter.IsLertaroUri("lertaro://search"));

    [TestMethod]
    public void IsLertaroUri_SchemeIsCaseInsensitive() => Assert.IsTrue(UriRouter.IsLertaroUri("Lertaro://search"));

    [TestMethod]
    public void IsLertaroUri_HttpScheme_ReturnsFalse() => Assert.IsFalse(UriRouter.IsLertaroUri("https://example.com"));

    [TestMethod]
    public void IsLertaroUri_NullOrEmpty_ReturnsFalse()
    {
        Assert.IsFalse(UriRouter.IsLertaroUri(null));
        Assert.IsFalse(UriRouter.IsLertaroUri(""));
    }

    [TestMethod]
    public void IsLertaroUri_RelativeUri_ReturnsFalse() => Assert.IsFalse(UriRouter.IsLertaroUri("search/foo"));

    [TestMethod]
    public void IsLertaroUri_MalformedUri_ReturnsFalse() => Assert.IsFalse(UriRouter.IsLertaroUri("not a uri at all"));

    [TestMethod]
    public void IsLertaroUri_UriWithPathAndArgs_ReturnsTrue() => Assert.IsTrue(UriRouter.IsLertaroUri("lertaro://settings/page/Index"));
}
