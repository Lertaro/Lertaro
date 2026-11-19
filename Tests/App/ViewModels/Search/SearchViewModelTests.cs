using Lertaro.App.ViewModels.Search;
using Lertaro.App.ViewModels.Search.Dispatch;
using Lertaro.App.ViewModels.Search.DynamicSidebar;
using Lertaro.App.ViewModels.Search.Mapping;
using Lertaro.PluginSdk.Abstractions.Plugins;
using System.Reflection;

namespace Lertaro.App.Tests.ViewModels.Search;

[STATestClass]
[DoNotParallelize]
public sealed class SearchViewModelTests
{
    [TestMethod]
    public void Dispose_RejectsContentThatWasQueuedBeforeTheWindowClosed()
    {
        var vm = new SearchViewModel();
        SearchQueryDispatchController dispatcher;
        try
        {
            const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
            dispatcher = (SearchQueryDispatchController)typeof(SearchViewModel).GetField("_dispatcher", fields)!.GetValue(vm)!;
            typeof(SearchQueryDispatchController).GetField("_accumulator", fields)!.SetValue(dispatcher,
                new StreamingResultAccumulator("report", resolveQueriedDirectory: _ => null));
            typeof(SearchQueryDispatchController).GetField("_searchSettled", fields)!.SetValue(dispatcher, true);
        }
        finally { vm.Dispose(); }

        dispatcher.ApplyContentRows([new AppSearchResult { FullPath = @"Z:\late.txt", IsFullSearchFileResult = true }], 0);

        Assert.IsEmpty(vm.FilteredResults);
    }

    [TestMethod]
    public async Task ContentArrivesWithTypeSelected_ClearingTypeRestoresItWithoutAnotherQuery()
    {
        using var vm = new SearchViewModel();
        var group = new DynamicSidebarGroupViewModel(new SidebarFilterGroup
        {
            Id = "Type", Items = [new SidebarFilterItem { Id = "Type_File", MatchPredicate = _ => true }]
        }, vm);
        vm.DynamicSidebarGroups.Add(group);
        group.Items[0].IsSelected = true;
        const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
        var dispatcher = (SearchQueryDispatchController)typeof(SearchViewModel).GetField("_dispatcher", fields)!.GetValue(vm)!;
        typeof(SearchQueryDispatchController).GetField("_accumulator", fields)!.SetValue(dispatcher,
            new StreamingResultAccumulator("report", resolveQueriedDirectory: _ => null));
        typeof(SearchQueryDispatchController).GetField("_searchSettled", fields)!.SetValue(dispatcher, true);
        var content = new AppSearchResult { Name = "report", FullPath = @"Z:\report.txt", IsFullSearchFileResult = true };

        dispatcher.ApplyContentRows([content], 0);
        await vm.PendingFilter;
        Assert.IsEmpty(vm.FilteredResults);
        group.Items[0].IsSelected = false;
        await vm.PendingFilter;

        Assert.AreSame(content, Assert.ContainsSingle(vm.FilteredResults));
        dispatcher.ApplyContentRows([new AppSearchResult { Name = "stale" }], -1);
        Assert.AreSame(content, Assert.ContainsSingle(vm.FilteredResults), "An obsolete provider batch is still rejected.");
    }

    [TestMethod]
    public void FilterAndSort_WhileQueryRuns_DoNotEndSearchState()
    {
        using var vm = new SearchViewModel();
        vm.SetQueryRunning(true);

        vm.OnDynamicFilterChanged();

        Assert.IsTrue(vm.IsSearching, "A completed filtering pass does not complete the index query.");
        vm.SetQueryRunning(false);
        Assert.IsFalse(vm.IsSearching);
    }

    [TestMethod]
    public void ClearQuery_EndsSearchStateAndClearsResults()
    {
        using var vm = new SearchViewModel();
        vm.SetQueryRunning(true);
        vm.FilteredResults.Add(new AppSearchResult { Name = "old", FullPath = @"Z:\old" });

        vm.AdvancedQuery = " ";

        Assert.IsFalse(vm.IsSearching);
        Assert.IsEmpty(vm.FilteredResults);
        Assert.IsTrue(vm.Hints.ShowWelcomeHint);
    }
}
