using Lertaro.Plugins.CoreExtensions.Providers.InstantAnswers;
using Lertaro.PluginSdk.Abstractions.Plugins;

namespace Lertaro.Plugins.CoreExtensions.Tests.Providers.InstantAnswers;

[TestClass]
public sealed class WebUrlInstantProviderTests
{
    private static readonly WebUrlInstantProvider Provider = new();

    /// <summary>The browser rows only: recognition tests below are about the https/http variants, and
    /// the appended ping row would only make their counts depend on a feature they do not exercise.</summary>
    private static List<InstantResultItem> BrowserRows(string query) =>
        Provider.GetInstantResults(query).Where(r => !IsPing(r)).ToList();

    private static bool IsPing(InstantResultItem item) => item.Title.StartsWith("ping ", StringComparison.Ordinal);

    [TestMethod]
    public void GetInstantResults_ValidHttpsUrl_ReturnsExecuteResult()
    {
        var result = BrowserRows("https://example.com").Single();

        Assert.AreEqual("https://example.com", result.Title);
        Assert.AreEqual("Execute", result.ActionType);
        Assert.AreEqual("https://example.com", result.ActionArgument);
    }

    [TestMethod]
    public void GetInstantResults_ValidHttpUrl_ReturnsExecuteResult() =>
        Assert.HasCount(1, BrowserRows("http://example.com/page"));

    [TestMethod]
    public void GetInstantResults_BareDomain_ReturnsHttpsThenHttpResults()
    {
        var results = BrowserRows("example.com/docs?q=1");

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
        var results = BrowserRows(input);

        Assert.HasCount(2, results);
        Assert.AreEqual($"https://{input}", results[0].ActionArgument);
        Assert.AreEqual($"http://{input}", results[1].ActionArgument);
    }

    [TestMethod]
    public void GetInstantResults_BareDomain_TrimsSurroundingWhitespace()
    {
        var results = BrowserRows("  www.example.com  ");

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
        Assert.HasCount(2, BrowserRows("WWW.Example.COM"));
        Assert.HasCount(2, BrowserRows("example.中国"));
    }

    [TestMethod]
    public void GetInstantResults_ExplicitScheme_IgnoresTheWhitelist()
    {
        const string input = "https://localhost:8080/report.pdf";

        var result = BrowserRows(input).Single();

        Assert.AreEqual(input, result.ActionArgument);
    }

    [TestMethod]
    public void GetInstantResults_BareIpAddress_ReturnsHttpsThenHttpResults()
    {
        var results = BrowserRows("192.168.1.10:3000");

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

    [TestMethod]
    public void GetInstantResults_AppendsOnePingRowAfterTheBrowserRows()
    {
        var results = Provider.GetInstantResults("example.com").ToList();

        Assert.HasCount(3, results);
        Assert.AreEqual(1, results.Count(IsPing));
        Assert.AreEqual("ping example.com", results[^1].Title);
        Assert.IsFalse(string.IsNullOrWhiteSpace(results[^1].Description));
        Assert.IsNotNull(results[^1].OnExecute);
    }

    [TestMethod]
    public void PingRow_StripsSchemeUserInfoPortPathQueryAndFragment()
    {
        Assert.AreEqual("ping baidu.com", Provider.GetInstantResults("https://baidu.com/").Last().Title);
        Assert.AreEqual("ping c.cn", Provider.GetInstantResults("https://user:pw@c.cn:8443/a/b?q=1#frag").Last().Title);
        Assert.AreEqual("ping www.example.com", Provider.GetInstantResults("http://www.example.com/page").Last().Title);
    }

    [TestMethod]
    public void PingRow_TakesBareAddressHostsAndDropsTheirPorts()
    {
        Assert.AreEqual("ping 192.168.1.10", Provider.GetInstantResults("192.168.1.10:3000").Last().Title);
        Assert.AreEqual("ping www.example.com", Provider.GetInstantResults("WWW.Example.COM").Last().Title);
    }

    [TestMethod]
    public void PingRow_TakesIpv6WithoutItsBrackets()
    {
        Assert.AreEqual("ping 2001:db8::1", Provider.GetInstantResults("https://[2001:db8::1]:8443/path").Last().Title);
        Assert.AreEqual("ping ::1", Provider.GetInstantResults("http://[::1]").Last().Title);
    }

    [TestMethod]
    public void PingRow_TakesPunycodeForAnInternationalisedHost() =>
        Assert.AreEqual("ping example.xn--fiqs8s", Provider.GetInstantResults("example.中国").Last().Title);

    [TestMethod]
    public void PingRow_UsesItsOwnLetterIcon()
    {
        var results = Provider.GetInstantResults("https://example.com").ToList();

        Assert.AreNotEqual(results[0].IconData, results[^1].IconData);
        // The counter of the "P" is a second subpath wound the same way as the outline, so it only
        // stays a hole because the path string asks for the even-odd fill rule.
        Assert.IsTrue(results[^1].IconData!.StartsWith("F0 ", StringComparison.Ordinal), results[^1].IconData);
    }
}
