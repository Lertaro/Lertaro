using Lertaro.App.Helpers;

namespace Lertaro.App.Tests.Helpers;

[TestClass]
public sealed class DomainUrlHelperTests
{
    [TestMethod]
    public void TryBuildWebUrls_BareDomain_ReturnsBothSchemes()
    {
        Assert.IsTrue(DomainUrlHelper.TryBuildWebUrls("example.com", out var httpsUrl, out var httpUrl));
        Assert.AreEqual("https://example.com", httpsUrl);
        Assert.AreEqual("http://example.com", httpUrl);
    }

    [TestMethod]
    public void TryBuildWebUrls_TrimsSurroundingWhitespace()
    {
        Assert.IsTrue(DomainUrlHelper.TryBuildWebUrls("  www.example.com  ", out var httpsUrl, out _));
        Assert.AreEqual("https://www.example.com", httpsUrl);
    }

    [TestMethod]
    public void TryBuildWebUrls_DomainWithPath_ReturnsBothSchemes()
    {
        Assert.IsTrue(DomainUrlHelper.TryBuildWebUrls("example.com/docs?q=1", out var httpsUrl, out var httpUrl));
        Assert.AreEqual("https://example.com/docs?q=1", httpsUrl);
        Assert.AreEqual("http://example.com/docs?q=1", httpUrl);
    }

    [TestMethod]
    public void TryBuildWebUrls_AllowsUserInfoPortQueryAndFragment()
    {
        Assert.IsTrue(DomainUrlHelper.TryBuildWebUrls("a:b@example.com:8443/docs?a=1&b=2#top", out var httpsUrl, out var httpUrl));
        Assert.AreEqual("https://a:b@example.com:8443/docs?a=1&b=2#top", httpsUrl);
        Assert.AreEqual("http://a:b@example.com:8443/docs?a=1&b=2#top", httpUrl);
    }

    [TestMethod]
    public void TryBuildWebUrls_AllowsUserInfoWithColon()
    {
        Assert.IsTrue(DomainUrlHelper.TryBuildWebUrls("a:b@c.cn/abc?a=%20f&b=xxx", out var httpsUrl, out var httpUrl));
        Assert.AreEqual("https://a:b@c.cn/abc?a=%20f&b=xxx", httpsUrl);
        Assert.AreEqual("http://a:b@c.cn/abc?a=%20f&b=xxx", httpUrl);

        Assert.IsTrue(DomainUrlHelper.TryBuildWebUrls("a:b@c.cn/?a=%20f", out httpsUrl, out httpUrl));
        Assert.AreEqual("https://a:b@c.cn/?a=%20f", httpsUrl);
        Assert.AreEqual("http://a:b@c.cn/?a=%20f", httpUrl);
    }

    [TestMethod]
    public void TryBuildWebUrls_AllowsUrlHostUnderscore()
        => Assert.IsTrue(DomainUrlHelper.TryBuildWebUrls("a_b.cn", out _, out _));

    [TestMethod]
    public void TryBuildWebUrls_AllowsIpHostsAndUnicodeDomains()
    {
        Assert.IsTrue(DomainUrlHelper.TryBuildWebUrls("127.0.0.1:8080/status", out var httpsUrl, out var httpUrl));
        Assert.AreEqual("https://127.0.0.1:8080/status", httpsUrl);
        Assert.AreEqual("http://127.0.0.1:8080/status", httpUrl);

        Assert.IsTrue(DomainUrlHelper.TryBuildWebUrls("[2001:db8::1]/status", out httpsUrl, out httpUrl));
        Assert.AreEqual("https://[2001:db8::1]/status", httpsUrl);
        Assert.AreEqual("http://[2001:db8::1]/status", httpUrl);

        Assert.IsTrue(DomainUrlHelper.TryBuildWebUrls("例子.中国/文档", out httpsUrl, out httpUrl));
        Assert.AreEqual("https://例子.中国/文档", httpsUrl);
        Assert.AreEqual("http://例子.中国/文档", httpUrl);
    }

    [TestMethod]
    public void TryBuildWebUrls_AlreadyUrl_ReturnsFalse()
        => Assert.IsFalse(DomainUrlHelper.TryBuildWebUrls("https://example.com", out _, out _));

    [TestMethod]
    public void TryBuildWebUrls_LocalPathOrSingleWord_ReturnsFalse()
    {
        Assert.IsFalse(DomainUrlHelper.TryBuildWebUrls(@"C:\\folder\\file.txt", out _, out _));
        Assert.IsFalse(DomainUrlHelper.TryBuildWebUrls("localhost", out _, out _));
    }

    [TestMethod]
    public void TryBuildWebUrls_InvalidDomainSyntax_ReturnsFalse()
    {
        Assert.IsFalse(DomainUrlHelper.TryBuildWebUrls("example..com", out _, out _));
        Assert.IsFalse(DomainUrlHelper.TryBuildWebUrls("example.com\\path", out _, out _));
        Assert.IsFalse(DomainUrlHelper.TryBuildWebUrls(@"a:b\@c.cn", out _, out _));
        Assert.IsFalse(DomainUrlHelper.TryBuildWebUrls("example.com blah", out _, out _));
        Assert.IsFalse(DomainUrlHelper.TryBuildWebUrls("a!.b.cn", out _, out _));
        Assert.IsFalse(DomainUrlHelper.TryBuildWebUrls("example.1", out _, out _));
        Assert.IsFalse(DomainUrlHelper.TryBuildWebUrls("256.1.1.1", out _, out _));
        Assert.IsFalse(DomainUrlHelper.TryBuildWebUrls("[2001:db8:::1]", out _, out _));
        Assert.IsFalse(DomainUrlHelper.TryBuildWebUrls("example.com:65536", out _, out _));
        Assert.IsFalse(DomainUrlHelper.TryBuildWebUrls("ftp://example.com", out _, out _));
    }
}
