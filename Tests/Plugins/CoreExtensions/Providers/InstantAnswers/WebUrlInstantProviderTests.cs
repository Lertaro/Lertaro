using Lertaro.Plugins.CoreExtensions.Providers.InstantAnswers;

namespace Lertaro.Plugins.CoreExtensions.Tests.Providers.InstantAnswers;

[TestClass]
public sealed class WebUrlInstantProviderTests
{
    private static readonly WebUrlInstantProvider Provider = new();

    [TestMethod]
    public void GetInstantResults_ValidHttpsUrl_ReturnsExecuteResult()
    {
        var result = Provider.GetInstantResults("https://example.com").Single();

        Assert.AreEqual("https://example.com", result.Title);
        Assert.AreEqual("Execute", result.ActionType);
        Assert.AreEqual("https://example.com", result.ActionArgument);
    }

    [TestMethod]
    public void GetInstantResults_ValidHttpUrl_ReturnsExecuteResult() =>
        Assert.HasCount(1, Provider.GetInstantResults("http://example.com/page").ToList());

    [TestMethod]
    public void GetInstantResults_UrlContainingSpace_ReturnsNothing() => Assert.IsEmpty(Provider.GetInstantResults("https://example.com /page"));

    [TestMethod]
    public void GetInstantResults_TooShort_ReturnsNothing() => Assert.IsEmpty(Provider.GetInstantResults("http://")); // 7 chars, under the 8-char minimum

    [TestMethod]
    public void GetInstantResults_NonHttpScheme_ReturnsNothing() => Assert.IsEmpty(Provider.GetInstantResults("ftp://example.com/file"));

    [TestMethod]
    public void GetInstantResults_PlainText_ReturnsNothing() => Assert.IsEmpty(Provider.GetInstantResults("just some search text"));

    [TestMethod]
    public void GetInstantResults_EmptyQuery_ReturnsNothing() => Assert.IsEmpty(Provider.GetInstantResults(""));

    [TestMethod]
    public void GetInstantResults_UrlWithNoHost_ReturnsNothing() => Assert.IsEmpty(Provider.GetInstantResults("https:///justapath"));
}
