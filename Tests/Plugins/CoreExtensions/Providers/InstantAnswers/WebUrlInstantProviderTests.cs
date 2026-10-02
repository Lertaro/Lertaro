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
    public void GetInstantResults_BareDomain_ReturnsHttpsThenHttpResults()
    {
        var results = Provider.GetInstantResults("example.com/docs?q=1").ToList();

        Assert.HasCount(2, results);
        Assert.AreEqual("https://example.com/docs?q=1", results[0].Title);
        Assert.AreEqual("http://example.com/docs?q=1", results[1].Title);
        Assert.IsFalse(string.IsNullOrWhiteSpace(results[0].Description));
        Assert.AreEqual(results[0].Description, results[1].Description);
    }

    [TestMethod]
    public void GetInstantResults_BareDomain_AllowsUserInfoPortQueryFragmentAndHyphen()
    {
        const string input = "a:b@c.cn:8443/docs/a-b?q=a-b&next=x-y#part-a";
        var results = Provider.GetInstantResults(input).ToList();

        Assert.HasCount(2, results);
        Assert.AreEqual($"https://{input}", results[0].ActionArgument);
        Assert.AreEqual($"http://{input}", results[1].ActionArgument);
    }

    [TestMethod]
    public void GetInstantResults_BareDomain_TrimsSurroundingWhitespace()
    {
        var results = Provider.GetInstantResults("  www.example.com  ").ToList();

        Assert.HasCount(2, results);
        Assert.AreEqual("https://www.example.com", results[0].Title);
        Assert.AreEqual("http://www.example.com", results[1].Title);
    }

    [TestMethod]
    public void GetInstantResults_UrlContainingSpace_ReturnsNothing() => Assert.IsEmpty(Provider.GetInstantResults("https://example.com /page"));

    [TestMethod]
    public void GetInstantResults_TooShort_ReturnsNothing() => Assert.IsEmpty(Provider.GetInstantResults("http://")); // 7 chars, under the 8-char minimum

    [TestMethod]
    public void GetInstantResults_NonHttpScheme_ReturnsNothing() => Assert.IsEmpty(Provider.GetInstantResults("ftp://example.com/file"));

    [TestMethod]
    public void GetInstantResults_FileName_ReturnsNothing()
    {
        // The suffix whitelist is what keeps a file search a file search. .md, .so, .app, .py and .sh are
        // real TLDs that are deliberately left out, because typing them means the file extension.
        var fileNames = new[]
        {
            "abc.txt", "report.pdf", "notes.md", "index.html", "photo.jpg", "data.json", "readme",
            "my.website-is-great", "libfoo.so", "Calculator.app", "main.py", "deploy.sh"
        };

        foreach (var fileName in fileNames)
            Assert.IsEmpty(Provider.GetInstantResults(fileName), fileName);
    }

    [TestMethod]
    public void GetInstantResults_SuffixNotWhitelistedButStartingWithOne_ReturnsNothing() =>
        Assert.IsEmpty(Provider.GetInstantResults("example.company"));

    [TestMethod]
    public void GetInstantResults_WhitelistedSuffix_IsMatchedCaseInsensitiveAndIncludesInternationalisedOnes()
    {
        Assert.HasCount(2, Provider.GetInstantResults("WWW.Example.COM").ToList());
        Assert.HasCount(2, Provider.GetInstantResults("example.中国").ToList());
    }

    [TestMethod]
    public void GetInstantResults_ExplicitScheme_IgnoresTheWhitelist()
    {
        const string input = "https://localhost:8080/report.pdf";

        var result = Provider.GetInstantResults(input).Single();

        Assert.AreEqual(input, result.ActionArgument);
    }

    [TestMethod]
    public void GetInstantResults_BareIpAddress_ReturnsHttpsThenHttpResults()
    {
        var results = Provider.GetInstantResults("192.168.1.10:3000").ToList();

        Assert.HasCount(2, results);
        Assert.AreEqual("https://192.168.1.10:3000", results[0].ActionArgument);
    }

    [TestMethod]
    public void GetInstantResults_PlainText_ReturnsNothing() => Assert.IsEmpty(Provider.GetInstantResults("just some search text"));

    [TestMethod]
    public void GetInstantResults_EmptyQuery_ReturnsNothing() => Assert.IsEmpty(Provider.GetInstantResults(""));

    [TestMethod]
    public void GetInstantResults_UrlWithNoHost_ReturnsNothing() => Assert.IsEmpty(Provider.GetInstantResults("https:///justapath"));

    [TestMethod]
    public void GetInstantResults_InvalidBareDomain_ReturnsNothing()
    {
        var invalidQueries = new[]
        {
            "example..com",
            "example.com\\path",
            @"a:b\@c.cn",
            "example.com blah",
            "a!.b.cn",
            "example.1",
            "256.1.1.1",
            "[2001:db8:::1]",
            "example.com:65536"
        };

        foreach (var query in invalidQueries)
            Assert.IsEmpty(Provider.GetInstantResults(query));
    }
}
