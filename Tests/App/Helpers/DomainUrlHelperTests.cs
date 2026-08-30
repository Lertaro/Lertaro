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
        Assert.IsFalse(DomainUrlHelper.TryBuildHttpsUrl("example.com blah", out _));
        Assert.IsFalse(DomainUrlHelper.TryBuildHttpsUrl("ftp://example.com", out _));
    }
}
