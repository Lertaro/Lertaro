using Lertaro.App.ViewModels.Search;

namespace Lertaro.App.Tests.ViewModels.Search;

[STATestClass]
[DoNotParallelize]
public sealed class SearchViewModelTests
{
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
        Assert.IsTrue(vm.ShowWelcomeHint);
    }
}
