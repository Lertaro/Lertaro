using System.IO;
using Lertaro.App.Helpers;
using Lertaro.PluginSdk.Abstractions.Plugins;

namespace Lertaro.App.Tests.Helpers;

// Enter on a quick-window result resolves the row against the current search-box text before executing
// it (SearchResultExecutionHelper). A per-type trigger character is stripped before the mappers run, so
// the row carries "环境" while the box still holds "，环境" -- treated as one raw string, every
// triggered row looked stale, and the refresh could not re-collect a searchable-item row at all, so
// Enter did nothing (silently, with the key already marked handled).
[TestClass]
public sealed class SearchResultExecutionHelperTests
{
    private static readonly IReadOnlyDictionary<string, string> CommaTriggers =
        new Dictionary<string, string> { ["settings-type-id"] = "，" };

    private static AppSearchResult SettingsRow(string searchQuery) => new()
    {
        Name = "环境变量",
        FullPath = "__SEARCHABLE_ITEM__:系统设置搜索:环境变量",
        ResultKind = "InstantResult",
        SearchQuery = searchQuery,
        SourceProvider = new FakeSearchableItemProvider(),
    };

    [TestMethod]
    public void SearchableRowUnderATrigger_IsResolvedAgainstTheStrippedQuery()
    {
        // The whole bug: this comparison is what decided whether the on-screen row was still current.
        Assert.AreEqual("环境",
            SearchResultExecutionHelper.ResolveSearchQuery(SettingsRow("环境"), "，环境", isInlineWindow: false, CommaTriggers));
    }

    [TestMethod]
    public void SearchableRowWithNoTriggerForItsFirstCharacter_KeepsTheQueryAsTyped()
    {
        Assert.AreEqual("环境",
            SearchResultExecutionHelper.ResolveSearchQuery(SettingsRow("环境"), "环境", isInlineWindow: false, CommaTriggers));
    }

    [TestMethod]
    public void SearchableRowInTheInlineWindow_KeepsTheRawQuery()
    {
        // The inline window has no concept of a per-type trigger, so stripping one there would search
        // one character short.
        Assert.AreEqual("，环境",
            SearchResultExecutionHelper.ResolveSearchQuery(SettingsRow("环境"), "，环境", isInlineWindow: true, CommaTriggers));
    }

    [TestMethod]
    public void InstantProviderRow_KeepsTheRawQuery()
    {
        // BuildQuickResults deliberately hands instant-result plugins the raw text, so a row that came
        // from one must be refreshed with that same text.
        var row = new AppSearchResult
        {
            Name = "10",
            FullPath = "__INSTANT_RESULT__:Calculator:10",
            ResultKind = "InstantResult",
            SearchQuery = "5+5",
            SourceProvider = new FakeInstantResultProvider(),
        };
        Assert.AreEqual("，5+5",
            SearchResultExecutionHelper.ResolveSearchQuery(row, "，5+5", isInlineWindow: false, CommaTriggers));
    }

    [TestMethod]
    public void PluginActionRow_KeepsTheRawQuery()
    {
        var row = SettingsRow("环境");
        row.ResultKind = "PluginAction";
        Assert.AreEqual("，环境",
            SearchResultExecutionHelper.ResolveSearchQuery(row, "，环境", isInlineWindow: false, CommaTriggers));
    }

    [TestMethod]
    public void CurrentRows_AreReturnedAsThemselves()
    {
        // A plain file row never needs refreshing, whatever the box holds.
        var fileRow = new AppSearchResult { Name = "a.txt", FullPath = @"C:\a.txt", SearchQuery = "旧" };
        Assert.AreSame(fileRow, SearchResultExecutionHelper.ResolveCurrent(fileRow, "新", isInlineWindow: false));

        // An instant row whose query already matches the box text is current by definition.
        var settingsRow = SettingsRow("环境");
        Assert.AreSame(settingsRow, SearchResultExecutionHelper.ResolveCurrent(settingsRow, "环境", isInlineWindow: false));
    }

    [TestMethod]
    public void AStaleSearchableRow_IsRefreshedFromTheSearchableItemMapper()
    {
        // The other half of the fix: the refresh used to ask only the instant-result providers, which
        // never produce an ISearchableItemProvider row, so a stale one resolved to null.
        var helper = Source("App/Helpers/SearchResultExecutionHelper.cs");
        var refresh = Between(helper, "var current = new List<AppSearchResult>();", "return current.FirstOrDefault");

        Assert.Contains("SearchableItemMapper.CollectSearchableItemResults", refresh,
            "a searchable-item row must be re-collected from the mapper that produced it");
        Assert.Contains("result.SourceProvider is ISearchableItemProvider", refresh,
            "and that branch must be taken by provider, not by guessing from the path");
    }

    private static string Source(string relativePath) =>
        File.ReadAllText(Path.Combine(RepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar)));

    private static string Between(string source, string from, string to)
    {
        var start = source.IndexOf(from, StringComparison.Ordinal);
        Assert.IsGreaterThan(-1, start, $"could not find '{from}'");
        var end = source.IndexOf(to, start + from.Length, StringComparison.Ordinal);
        Assert.IsGreaterThan(-1, end, $"could not find '{to}' after '{from}'");
        return source.Substring(start, end - start);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AGENTS.md")))
            dir = dir.Parent;
        Assert.IsNotNull(dir, "could not locate the repository root");
        return dir!.FullName;
    }

    private sealed class FakeSearchableItemProvider : ISearchableItemProvider
    {
        public event Action? ItemsChanged { add { } remove { } }
        public IEnumerable<SearchableItem> GetSearchableItems() => Array.Empty<SearchableItem>();
    }

    private sealed class FakeInstantResultProvider : IInstantResultProvider
    {
        public IEnumerable<InstantResultItem> GetInstantResults(string query) => Array.Empty<InstantResultItem>();
    }
}
