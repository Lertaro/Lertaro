using Lertaro.App.ViewModels.Settings;

namespace Lertaro.App.Tests.ViewModels.Settings;

[TestClass]
public sealed class HistoryListViewModelTests
{
    private static HistoryListViewModel<string> MakeVm(
        List<string>? entries = null,
        bool enabled = true,
        Action<bool>? setEnabled = null)
    {
        var enabledFlag = enabled;
        return new HistoryListViewModel<string>(
            () => entries ?? new List<string>(),
            raw => new HistoryEntryViewModel<string> { RawValue = raw, Primary = raw, Secondary = "" },
            () => enabledFlag,
            v => { enabledFlag = v; setEnabled?.Invoke(v); });
    }

    [TestMethod]
    public void Constructor_MapsLoadedEntriesIntoFilteredItems()
    {
        var vm = MakeVm(new List<string> { "a.txt", "b.txt" });

        Assert.HasCount(2, vm.FilteredItems);
        Assert.AreEqual("a.txt", vm.FilteredItems[0].Primary);
    }

    [TestMethod]
    public void IsHistoryEnabled_Get_DelegatesToGetEnabled() =>
        Assert.IsTrue(MakeVm(enabled: true).IsHistoryEnabled);

    [TestMethod]
    public void IsHistoryEnabled_SetDifferentValue_InvokesSetEnabled()
    {
        var invoked = false;
        var vm = MakeVm(enabled: true, setEnabled: v => invoked = v == false);

        vm.IsHistoryEnabled = false;

        Assert.IsTrue(invoked);
    }

    [TestMethod]
    public void IsHistoryEnabled_SetSameValue_DoesNotInvokeSetEnabled()
    {
        var invoked = false;
        var vm = MakeVm(enabled: true, setEnabled: _ => invoked = true);

        vm.IsHistoryEnabled = true;

        Assert.IsFalse(invoked);
    }

    [TestMethod]
    public void RemoveItemCommand_Execute_RemovesFromFilteredItems()
    {
        var vm = MakeVm(new List<string> { "a.txt", "b.txt" });
        var item = vm.FilteredItems[0];

        vm.RemoveItemCommand.Execute(item);

        Assert.HasCount(1, vm.FilteredItems);
        Assert.AreEqual("b.txt", vm.FilteredItems[0].Primary);
    }

    [TestMethod]
    public void RemoveItemCommand_Execute_RemovedItemIsExcludedFromGetEntriesToSave()
    {
        var vm = MakeVm(new List<string> { "a.txt", "b.txt" });
        vm.RemoveItemCommand.Execute(vm.FilteredItems[0]);

        CollectionAssert.AreEqual(new[] { "b.txt" }, vm.GetEntriesToSave().ToList());
    }

    [TestMethod]
    public void RemoveItemCommand_Execute_NullItem_DoesNothing()
    {
        var vm = MakeVm(new List<string> { "a.txt" });

        vm.RemoveItemCommand.Execute(null);

        Assert.HasCount(1, vm.FilteredItems);
    }

    [TestMethod]
    public void ClearAllCommand_Execute_EmptiesFilteredItemsAndEntriesToSave()
    {
        var vm = MakeVm(new List<string> { "a.txt", "b.txt" });

        vm.ClearAllCommand.Execute(null);

        Assert.IsEmpty(vm.FilteredItems);
        Assert.IsEmpty(vm.GetEntriesToSave());
    }

    [TestMethod]
    public void SearchText_MatchingSubstring_FiltersToMatchingItemsOnly()
    {
        var vm = MakeVm(new List<string> { "report.docx", "budget.xlsx" });

        vm.SearchText = "report";

        Assert.HasCount(1, vm.FilteredItems);
        Assert.AreEqual("report.docx", vm.FilteredItems[0].Primary);
    }

    [TestMethod]
    public void SearchText_ClearedAfterFiltering_RestoresAllItems()
    {
        var vm = MakeVm(new List<string> { "report.docx", "budget.xlsx" });
        vm.SearchText = "report";

        vm.SearchText = "";

        Assert.HasCount(2, vm.FilteredItems);
    }

    [TestMethod]
    public void SearchText_NoMatch_ResultsInEmptyFilteredItems()
    {
        var vm = MakeVm(new List<string> { "report.docx" });

        vm.SearchText = "zzz_no_match_zzz";

        Assert.IsEmpty(vm.FilteredItems);
    }

    [TestMethod]
    public void GetEntriesToSave_ReflectsEditedOrderNotOriginalLoadOrder()
    {
        var vm = MakeVm(new List<string> { "a.txt", "b.txt", "c.txt" });
        vm.RemoveItemCommand.Execute(vm.FilteredItems.Single(x => x.Primary == "b.txt"));

        CollectionAssert.AreEqual(new[] { "a.txt", "c.txt" }, vm.GetEntriesToSave().ToList());
    }
}
