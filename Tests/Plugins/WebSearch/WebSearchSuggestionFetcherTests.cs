namespace Lertaro.Plugins.WebSearch.Tests;

// Bodies captured from the live endpoints (2026-10-20), trimmed to a few items -- kept verbatim in key
// layout and markup, because the shape is the whole contract these tests exist for.
[TestClass]
public sealed class WebSearchSuggestionFetcherTests
{
    private const string BilibiliMainBody = """
        {"exp_str":"","code":0,"result":{"tag":[
          {"value":"手机","term":"手机","ref":0,"name":"<em class=\"suggest_high_light\">手机</em>","spid":5,"type":"","item_feature":""},
          {"value":"手机推荐2026双十一","term":"手机推荐2026双十一","ref":0,"name":"<em class=\"suggest_high_light\">手机</em>推荐2026双十一","spid":5,"type":"","item_feature":""},
          {"value":"手机壁纸","term":"手机壁纸","ref":0,"name":"<em class=\"suggest_high_light\">手机</em>壁纸","spid":5,"type":"","item_feature":""}
        ]}}
        """;

    private const string BilibiliApiBody = """
        {"code":0,"message":"OK","ttl":1,"data":{"exp_str":"","code":0,"result":{"tag":[
          {"value":"手机","term":"手机","ref":0,"name":"\u003cem class=\"suggest_high_light\"\u003e手机\u003c/em\u003e","spid":5,"type":""},
          {"value":"手机壁纸","term":"手机壁纸","ref":0,"name":"\u003cem class=\"suggest_high_light\"\u003e手机\u003c/em\u003e壁纸","spid":5,"type":""}
        ]}}}
        """;

    private const string OpenSearchBody = """["手机",["手机银行","手机管家","手机警报器","手机壁纸"]]""";

    [TestMethod]
    public void ParseSuggestions_BilibiliMainEndpoint_ReadsValuesAndDropsTheTypedWord()
    {
        var suggestions = WebSearchSuggestionFetcher.ParseSuggestions(BilibiliMainBody, "手机");

        CollectionAssert.AreEqual(new List<string> { "手机推荐2026双十一", "手机壁纸" }, suggestions);
    }

    [TestMethod]
    public void ParseSuggestions_BilibiliApiEndpoint_ReachesThroughTheExtraWrapper()
    {
        var suggestions = WebSearchSuggestionFetcher.ParseSuggestions(BilibiliApiBody, "手机");

        CollectionAssert.AreEqual(new List<string> { "手机壁纸" }, suggestions);
    }

    [TestMethod]
    public void ParseSuggestions_OpenSearchBody_TakesTheSecondArrayNotTheFirstItem()
    {
        var suggestions = WebSearchSuggestionFetcher.ParseSuggestions(OpenSearchBody, "shou");

        CollectionAssert.AreEqual(new List<string> { "手机银行", "手机管家", "手机警报器", "手机壁纸" }, suggestions);
    }

    // Not a captured body: no endpoint measured here needs it, but "name" is the last key tried, and an
    // item that only has it arrives wrapped in markup and, for some sites, with newlines inside.
    [TestMethod]
    public void ParseSuggestions_MarkupOnlyItems_AreCleanedToOneLine()
    {
        const string body = """{"suggestions":[{"name":"<em>fo</em>o bar"},{"name":"<em>fo</em>o\nbaz"}]}""";

        CollectionAssert.AreEqual(new List<string> { "foo bar", "foo baz" }, WebSearchSuggestionFetcher.ParseSuggestions(body, "fo"));
    }

    [TestMethod]
    public void ParseSuggestions_RepeatsAndOverlongItems_AreDropped()
    {
        var overlong = new string('z', 201);
        var body = $$"""{"a":[{"value":"x"},{"value":"X"},{"value":"y"}],"b":["{{overlong}}"]}""";

        CollectionAssert.AreEqual(new List<string> { "x", "y" }, WebSearchSuggestionFetcher.ParseSuggestions(body, ""));
    }

    // An endpoint that answers with a list of plain strings rather than objects (Google's chrome client
    // wraps them the same way OpenSearch does, but a bare list of strings has no typed word in front).
    [TestMethod]
    public void ParseSuggestions_ArrayOfStrings_IsReadDirectly()
    {
        const string body = """{"data":["alpha","beta"]}""";

        CollectionAssert.AreEqual(new List<string> { "alpha", "beta" }, WebSearchSuggestionFetcher.ParseSuggestions(body, "al"));
    }
}
