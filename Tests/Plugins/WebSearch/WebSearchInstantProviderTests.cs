using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.WebSearch.Tests;

[TestClass]
public sealed class WebSearchInstantProviderTests
{
    [TestMethod]
    public void BuildUrl_PercentSPlaceholder_ReplacesWithEncodedTerm() =>
        Assert.AreEqual("https://x.com/?q=hello%20world", WebSearchInstantProvider.BuildUrl("https://x.com/?q=%s", "hello world"));

    [TestMethod]
    public void BuildUrl_Format0Placeholder_ReplacesWithEncodedTerm() =>
        Assert.AreEqual("https://x.com/?q=hello%20world", WebSearchInstantProvider.BuildUrl("https://x.com/?q={0}", "hello world"));

    [TestMethod]
    public void BuildUrl_NoPlaceholder_AppendsEncodedTermToEnd() =>
        Assert.AreEqual("https://x.com/search/hello", WebSearchInstantProvider.BuildUrl("https://x.com/search/", "hello"));

    [TestMethod]
    public void BuildUrl_TermWithReservedChars_IsPercentEncoded() =>
        Assert.AreEqual("https://x.com/?q=a%26b", WebSearchInstantProvider.BuildUrl("https://x.com/?q=%s", "a&b"));
}

// PluginSettingsService.GetSettingFunc is a shared static delegate, and WebSearchInstantProvider caches
// what it loads in a private static field with no public reset -- NotifySettingChanged is the only
// available seam to bust that cache between tests (it's exactly the mechanism the host itself uses when
// settings are saved). [DoNotParallelize] keeps tests in this class from racing on either.
[TestClass]
[DoNotParallelize]
public sealed class WebSearchInstantProviderSettingsTests
{
    private const string PluginId = "Lertaro.Plugins.WebSearch";

    [TestInitialize]
    public void Reset()
    {
        PluginSettingsService.GetSettingFunc = null;
        PluginSettingsService.NotifySettingChanged(PluginId, "SearchSources");
    }

    private static void ConfigureSources(List<WebSearchInstantProvider.SearchSourceItem> sources) =>
        PluginSettingsService.GetSettingFunc = (pluginId, key, defaultValue) =>
            pluginId == PluginId && key == "SearchSources" ? sources : defaultValue;

    private static WebSearchInstantProvider.SearchSourceItem MakeSource(string keyword = "g", string url = "https://x.com/?q=%s", string suggestUrl = "") =>
        new() { Name = "TestEngine", Keyword = keyword, Url = url, SuggestUrl = suggestUrl };

    [TestMethod]
    public void GetInstantResults_EmptyQuery_ReturnsNothing() =>
        Assert.IsEmpty(new WebSearchInstantProvider().GetInstantResults(""));

    [TestMethod]
    public void GetInstantResults_NoConfiguredSources_ReturnsNothing() =>
        Assert.IsEmpty(new WebSearchInstantProvider().GetInstantResults("g hello"));

    [TestMethod]
    public void GetInstantResults_NonMatchingKeyword_ReturnsNothing()
    {
        ConfigureSources(new() { MakeSource(keyword: "g") });

        Assert.IsEmpty(new WebSearchInstantProvider().GetInstantResults("b hello"));
    }

    [TestMethod]
    public void GetInstantResults_MatchingKeywordWithTerm_ReturnsExecuteResultWithBuiltUrl()
    {
        ConfigureSources(new() { MakeSource(keyword: "g", url: "https://x.com/?q=%s") });

        var result = new WebSearchInstantProvider().GetInstantResults("g hello world").Single();

        Assert.AreEqual("Execute", result.ActionType);
        Assert.AreEqual("https://x.com/?q=hello%20world", result.ActionArgument);
    }

    [TestMethod]
    public void GetInstantResults_MatchingKeywordNoTerm_ReturnsPlaceholderWithNoAction()
    {
        // The keyword match requires "keyword " (with the trailing space) -- a bare "g" with no
        // space never matches at all, so the empty-term case is "g " (trimmed term is empty).
        ConfigureSources(new() { MakeSource(keyword: "g") });

        var result = new WebSearchInstantProvider().GetInstantResults("g ").Single();

        Assert.AreEqual("None", result.ActionType);
    }

    [TestMethod]
    public void GetInstantResults_KeywordMatchIsCaseInsensitive()
    {
        ConfigureSources(new() { MakeSource(keyword: "Google") });

        Assert.HasCount(1, new WebSearchInstantProvider().GetInstantResults("google hello").ToList());
    }

    [TestMethod]
    public void GetInstantResults_NoSuggestUrl_YieldsOnlyTheMainResult()
    {
        ConfigureSources(new() { MakeSource(keyword: "g", suggestUrl: "") });

        var results = new WebSearchInstantProvider().GetInstantResults("g hello").ToList();

        Assert.HasCount(1, results);
    }

    [TestMethod]
    public void GetHighlightMask_NonMatchingKeyword_ReturnsNull()
    {
        ConfigureSources(new() { MakeSource(keyword: "g") });

        Assert.IsNull(new WebSearchInstantProvider().GetHighlightMask("some title", "b hello"));
    }

    [TestMethod]
    public void GetHighlightMask_MatchingKeywordNoSearchTerm_ReturnsAllFalseMask()
    {
        ConfigureSources(new() { MakeSource(keyword: "g") });

        var mask = new WebSearchInstantProvider().GetHighlightMask("some title", "g ");

        Assert.IsNotNull(mask);
        Assert.IsTrue(mask.All(b => !b));
    }

    [TestMethod]
    public void GetHighlightMask_EmptyQuery_ReturnsNull() =>
        Assert.IsNull(new WebSearchInstantProvider().GetHighlightMask("text", ""));
}
