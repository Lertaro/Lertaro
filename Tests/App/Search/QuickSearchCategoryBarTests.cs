using Lertaro.App.ViewModels.Search;
using Lertaro.App.ViewModels.Search.Mapping;
using Lertaro.Core;
using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Helpers;

namespace Lertaro.App.Tests.Search;

// The quick window's filter bar. Its tabs come from the plugin's type settings, so what is pinned here is the
// host's half: the one tab the host owns, the wrap-around over a list whose length the host does not decide,
// and the fact that a tab's own predicate is what narrows the page before the display cap counts it.
[TestClass]
public sealed class QuickSearchCategoryBarTests
{
    [TestMethod]
    public void Categories_StartWithTheHostsOwnUnfilteredTab()
    {
        using var vm = new QuickSearchViewModel();

        Assert.AreEqual(string.Empty, vm.Categories[0].Id, "the host's 全部 tab is first and belongs to no plugin");
        Assert.IsNull(vm.Categories[0].Category, "an unfiltered tab must not narrow the index walk");
        Assert.AreSame(vm.Categories[0], vm.SelectedCategory, "and it is what the bar opens on");
        Assert.IsTrue(vm.Categories[0].MatchPredicate(new Row("anything.xyz")), "it keeps every row");
    }

    [TestMethod]
    public void CycleCategory_WrapsAtBothEndsOverWhateverThePluginsListed()
    {
        using var vm = new QuickSearchViewModel();
        var count = vm.Categories.Count;

        vm.CycleCategory(-1);
        Assert.AreEqual(vm.Categories[0], vm.SelectedCategory, "stepping back from the first tab lands on the last");

        for (var i = 0; i < count; i++)
            vm.CycleCategory(1);
        Assert.AreEqual(vm.Categories[0], vm.SelectedCategory, "a full cycle returns to the start");
    }

    [TestMethod]
    public void RebuildCategories_KeepsTheSelectedTabByIdNotByReference()
    {
        using var vm = new QuickSearchViewModel();
        var before = vm.SelectedCategory;

        // Providers hand back new instances on every call, so identity has to be the row's id: a user
        // mid-filter would otherwise be bounced to 全部 by any refresh under them.
        vm.Search.RebuildCategories();

        Assert.AreNotSame(before, vm.SelectedCategory, "the row object is a new one after a rebuild");
        Assert.AreEqual(before.Id, vm.SelectedCategory.Id, "and it is the same tab");
    }

    [TestMethod]
    public void ResetCategoryFilter_LeavesWhateverTabTheBarWasOn()
    {
        // The quick window calls this on every hide unless the user opted into remembering the tab, so what is
        // worth pinning is that the reset lands on the host's own tab and takes the narrowing off the pipeline
        // with it -- a summon that reads 全部 but filters as 文档 is the failure this exists to prevent.
        using var vm = new QuickSearchViewModel();
        vm.SelectedCategory = new SidebarFilterItem
        {
            Id = "Type_Audio",
            DisplayName = "音频",
            MatchPredicate = _ => false,
        };
        Assert.IsNotNull(vm.ActiveCategoryFilter, "the stand-in tab really does narrow");

        vm.Search.ResetCategoryFilter();

        Assert.AreSame(vm.Categories[0], vm.SelectedCategory);
        Assert.IsNull(vm.ActiveCategoryFilter);
    }

    [TestMethod]
    public void ActiveCategoryFilter_IsNullOnlyForTheHostsOwnTab()
    {
        // The bar's first tab means "no filter", which the pipeline reads as null. A predicate that happens to
        // keep every row is a different thing: it would switch the display cap off for an unfiltered search.
        using var vm = new QuickSearchViewModel();
        Assert.IsNull(vm.ActiveCategoryFilter);

        if (vm.Categories.Count > 1)
        {
            vm.SelectedCategory = vm.Categories[1];
            Assert.IsNotNull(vm.ActiveCategoryFilter, "any plugin row filters, even one wide as 文件");
        }
    }

    [TestMethod]
    public void BuildQuickResults_AFilterIsAppliedBeforeTheDisplayCap()
    {
        // The cap is what a filtered list must escape: the engine's own order puts documents ahead of audio
        // for a short query, so capping first and filtering after would hand the audio tab an empty list while
        // the index held seventy matches.
        var candidates = new List<SearchResult>();
        for (var i = 0; i < 120; i++)
            candidates.Add(Record($@"D:\mix\doc{i}.md"));
        for (var i = 0; i < 70; i++)
            candidates.Add(Record($@"D:\mix\song{i}.mp3"));

        var results = SearchResultMapper.BuildQuickResults(
            candidates, "song", scope: null, contextDirectory: null, isInlineWindow: false,
            categoryFilter: AudioRow().MatchPredicate);

        Assert.AreEqual(70, results.Count, "every audio match should survive, not just the ones inside the cap");
        Assert.IsTrue(results.All(row => AudioRow().MatchPredicate(row)));
        Assert.IsFalse(results.Any(row => row.FullPath.Contains("__SHOW_MORE__")),
            "a filtered list has no overflow left to offer");
    }

    [TestMethod]
    public void BuildQuickResults_TheUnfilteredTab_StillOffersItsOverflow()
    {
        // What the filtered case gives up: an unfiltered list hands its tail to the show-more row, because the
        // cap is what keeps that row's own list scrollable.
        var candidates = new List<SearchResult>();
        for (var i = 0; i < 120; i++)
            candidates.Add(Record($@"D:\mix\doc{i}.md"));

        var results = SearchResultMapper.BuildQuickResults(
            candidates, "doc", scope: null, contextDirectory: null, isInlineWindow: false,
            categoryFilter: null);

        Assert.IsTrue(results.Any(row => row.FullPath == "__SHOW_MORE__"),
            "an unfiltered list hands its tail to the show-more row; a filtered one has none left to offer");
        Assert.IsTrue(results.Any(row => row.FullPath.EndsWith(".md")), "and it keeps every kind");
    }

    // What the plugin's audio row carries: the category the engine narrows by, and a predicate that answers
    // for the rows the engine could not classify at the source.
    private static SidebarFilterItem AudioRow() => new()
    {
        Id = "Type_Audio",
        DisplayName = "音频",
        Category = SearchCategoryFilter.Audio,
        MatchPredicate = row => SearchCategoryClassifier.Categorize(row) == SearchCategoryFilter.Audio,
    };

    private static SearchResult Record(string path) => new()
    {
        Path = path,
        Name = System.IO.Path.GetFileName(path),
        IsDir = false,
        Drive = "D",
    };

    private sealed class Row(string path) : ISearchResult
    {
        public string Name { get; } = System.IO.Path.GetFileName(path);
        public string FullPath { get; } = path;
        public string ContextDirectory { get; } = string.Empty;
        public bool IsDir { get; }
        public bool IsApplication { get; }
    }
}
