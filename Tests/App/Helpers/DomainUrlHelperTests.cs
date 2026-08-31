using Lertaro.App.Helpers;

namespace Lertaro.App.Tests.Helpers;

[TestClass]
public sealed class DomainUrlHelperTests
{
    [TestMethod]
    public void TryBuildHttpsUrl_BareDomain_ReturnsHttpsUrl()
    {
        Assert.IsTrue(DomainUrlHelper.TryBuildHttpsUrl("example.com", out var url));
        Assert.AreEqual("https://example.com", url);
    }

    [TestMethod]
    public void TryBuildHttpsUrl_TrimsSurroundingWhitespace()
    {
        Assert.IsTrue(DomainUrlHelper.TryBuildHttpsUrl("  www.example.com  ", out var url));
        Assert.AreEqual("https://www.example.com", url);
    }

    [TestMethod]
    public void TryBuildHttpsUrl_DomainWithPath_ReturnsHttpsUrl()
    {
        Assert.IsTrue(DomainUrlHelper.TryBuildHttpsUrl("example.com/docs?q=1", out var url));
        Assert.AreEqual("https://example.com/docs?q=1", url);
    }

    [TestMethod]
    public void TryBuildHttpsUrl_AllowsUserInfoPortQueryAndFragment()
    {
        Assert.IsTrue(DomainUrlHelper.TryBuildHttpsUrl("a:b@example.com:8443/docs?a=1&b=2#top", out var url));
        Assert.AreEqual("https://a:b@example.com:8443/docs?a=1&b=2#top", url);
    }

    [TestMethod]
    public void TryBuildHttpsUrl_AllowsUserInfoWithColon()
    {
        Assert.IsTrue(DomainUrlHelper.TryBuildHttpsUrl("a:b@c.cn/abc?a=%20f&b=xxx", out var url));
        Assert.AreEqual("https://a:b@c.cn/abc?a=%20f&b=xxx", url);

        Assert.IsTrue(DomainUrlHelper.TryBuildHttpsUrl("a:b@c.cn/?a=%20f", out url));
        Assert.AreEqual("https://a:b@c.cn/?a=%20f", url);
    }

    [TestMethod]
    public void TryBuildHttpsUrl_AllowsUrlHostUnderscore()
        => Assert.IsTrue(DomainUrlHelper.TryBuildHttpsUrl("a_b.cn", out _));

    [TestMethod]
    public void TryBuildHttpsUrl_AllowsIpHostsAndUnicodeDomains()
    {
        Assert.IsTrue(DomainUrlHelper.TryBuildHttpsUrl("127.0.0.1:8080/status", out var url));
        Assert.AreEqual("https://127.0.0.1:8080/status", url);

        Assert.IsTrue(DomainUrlHelper.TryBuildHttpsUrl("[2001:db8::1]/status", out url));
        Assert.AreEqual("https://[2001:db8::1]/status", url);

        Assert.IsTrue(DomainUrlHelper.TryBuildHttpsUrl("例子.中国/文档", out url));
        Assert.AreEqual("https://例子.中国/文档", url);
    }

    [TestMethod]
    public void TryBuildHttpsUrl_AlreadyUrl_ReturnsFalse()
        => Assert.IsFalse(DomainUrlHelper.TryBuildHttpsUrl("https://example.com", out _));

    [TestMethod]
    public void TryBuildHttpsUrl_LocalPathOrSingleWord_ReturnsFalse()
    {
        Assert.IsFalse(DomainUrlHelper.TryBuildHttpsUrl(@"C:\\folder\\file.txt", out _));
        Assert.IsFalse(DomainUrlHelper.TryBuildHttpsUrl("localhost", out _));
    }

    [TestMethod]
    public void TryBuildHttpsUrl_InvalidDomainSyntax_ReturnsFalse()
    {
        Assert.IsFalse(DomainUrlHelper.TryBuildHttpsUrl("example..com", out _));
        Assert.IsFalse(DomainUrlHelper.TryBuildHttpsUrl("example.com\\path", out _));
        Assert.IsFalse(DomainUrlHelper.TryBuildHttpsUrl(@"a:b\@c.cn", out _));
        Assert.IsFalse(DomainUrlHelper.TryBuildHttpsUrl("example.com blah", out _));
        Assert.IsFalse(DomainUrlHelper.TryBuildHttpsUrl("a!.b.cn", out _));
        Assert.IsFalse(DomainUrlHelper.TryBuildHttpsUrl("example.1", out _));
        Assert.IsFalse(DomainUrlHelper.TryBuildHttpsUrl("256.1.1.1", out _));
        Assert.IsFalse(DomainUrlHelper.TryBuildHttpsUrl("[2001:db8:::1]", out _));
        Assert.IsFalse(DomainUrlHelper.TryBuildHttpsUrl("example.com:65536", out _));
        Assert.IsFalse(DomainUrlHelper.TryBuildHttpsUrl("ftp://example.com", out _));
    }
}
