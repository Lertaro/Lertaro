using Lertaro.Core;
using Lertaro.App.ViewModels.Settings.General;

namespace Lertaro.App.Tests.ViewModels.Settings.General;

[TestClass]
public sealed class ColumnOrderViewModelTests
{
    // The constructor's own enumeration of PluginManager.Instance.ResultColumnProviders isn't exercised
    // here (no seam, matching this codebase's existing PluginManager-registry-untested convention) --
    // these tests instead seed Items directly to cover MoveUp/MoveDown/Save in isolation.
    private static ColumnOrderViewModel MakeViewModel(UserSettings settings, params (string Id, string Name)[] items)
    {
        var vm = new ColumnOrderViewModel(settings);
        vm.Items.Clear();
        foreach (var (id, name) in items)
            vm.Items.Add(new ColumnOrderItem(id, () => name));
        return vm;
    }

    [TestMethod]
    public void MoveUpCommand_OnFirstItem_DoesNotMove()
    {
        var vm = MakeViewModel(new UserSettings(), ("Name", "Name"), ("Path", "Path"));

        vm.MoveUpCommand.Execute(vm.Items[0]);

        CollectionAssert.AreEqual(new[] { "Name", "Path" }, vm.Items.Select(x => x.Id).ToList());
    }

    [TestMethod]
    public void MoveUpCommand_OnSecondItem_SwapsWithFirst()
    {
        var vm = MakeViewModel(new UserSettings(), ("Name", "Name"), ("Path", "Path"));

        vm.MoveUpCommand.Execute(vm.Items[1]);

        CollectionAssert.AreEqual(new[] { "Path", "Name" }, vm.Items.Select(x => x.Id).ToList());
    }

    [TestMethod]
    public void MoveDownCommand_OnLastItem_DoesNotMove()
    {
        var vm = MakeViewModel(new UserSettings(), ("Name", "Name"), ("Path", "Path"));

        vm.MoveDownCommand.Execute(vm.Items[1]);

        CollectionAssert.AreEqual(new[] { "Name", "Path" }, vm.Items.Select(x => x.Id).ToList());
    }

    [TestMethod]
    public void MoveDownCommand_OnFirstItem_SwapsWithSecond()
    {
        var vm = MakeViewModel(new UserSettings(), ("Name", "Name"), ("Path", "Path"));

        vm.MoveDownCommand.Execute(vm.Items[0]);

        CollectionAssert.AreEqual(new[] { "Path", "Name" }, vm.Items.Select(x => x.Id).ToList());
    }

    [TestMethod]
    public void Save_WritesItemIdsInOrderToUserSettings()
    {
        var settings = new UserSettings();
        var vm = MakeViewModel(settings, ("DateModified", "Date"), ("Name", "Name"), ("Path", "Path"));

        vm.Save();

        CollectionAssert.AreEqual(new[] { "DateModified", "Name", "Path" }, settings.ColumnOrder);
    }
}
