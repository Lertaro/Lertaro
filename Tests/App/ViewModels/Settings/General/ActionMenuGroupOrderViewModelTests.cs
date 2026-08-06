using Lertaro.Core;
using Lertaro.App.ViewModels.Settings.General;

namespace Lertaro.App.Tests.ViewModels.Settings.General;

[TestClass]
public sealed class ActionMenuGroupOrderViewModelTests
{
    // The constructor's own enumeration of PluginManager.Instance.Actions/DynamicActionProviders isn't
    // exercised here (no seam, matching this codebase's existing PluginManager-registry-untested
    // convention -- see SidebarGroupOrderViewModelTests) -- these tests instead seed Items directly to
    // cover MoveUp/MoveDown/Save in isolation.
    private static ActionMenuGroupOrderViewModel MakeViewModel(UserSettings settings, params (string Id, string Name)[] items)
    {
        var vm = new ActionMenuGroupOrderViewModel(settings);
        vm.Items.Clear();
        foreach (var (id, name) in items)
            vm.Items.Add(new ActionMenuGroupOrderItem(id, () => name));
        return vm;
    }

    [TestMethod]
    public void MoveUpCommand_OnFirstItem_DoesNotMove()
    {
        var vm = MakeViewModel(new UserSettings(), ("__builtin__", "Common"), ("custom", "Custom Actions"));

        vm.MoveUpCommand.Execute(vm.Items[0]);

        CollectionAssert.AreEqual(new[] { "__builtin__", "custom" }, vm.Items.Select(x => x.Id).ToList());
    }

    [TestMethod]
    public void MoveUpCommand_OnSecondItem_SwapsWithFirst()
    {
        var vm = MakeViewModel(new UserSettings(), ("__builtin__", "Common"), ("custom", "Custom Actions"));

        vm.MoveUpCommand.Execute(vm.Items[1]);

        CollectionAssert.AreEqual(new[] { "custom", "__builtin__" }, vm.Items.Select(x => x.Id).ToList());
    }

    [TestMethod]
    public void MoveDownCommand_OnLastItem_DoesNotMove()
    {
        var vm = MakeViewModel(new UserSettings(), ("__builtin__", "Common"), ("custom", "Custom Actions"));

        vm.MoveDownCommand.Execute(vm.Items[1]);

        CollectionAssert.AreEqual(new[] { "__builtin__", "custom" }, vm.Items.Select(x => x.Id).ToList());
    }

    [TestMethod]
    public void MoveDownCommand_OnFirstItem_SwapsWithSecond()
    {
        var vm = MakeViewModel(new UserSettings(), ("__builtin__", "Common"), ("custom", "Custom Actions"));

        vm.MoveDownCommand.Execute(vm.Items[0]);

        CollectionAssert.AreEqual(new[] { "custom", "__builtin__" }, vm.Items.Select(x => x.Id).ToList());
    }

    [TestMethod]
    public void Save_WritesItemIdsInOrderToUserSettings()
    {
        var settings = new UserSettings();
        var vm = MakeViewModel(settings, ("custom", "Custom Actions"), ("__builtin__", "Common"));

        vm.Save();

        CollectionAssert.AreEqual(new[] { "custom", "__builtin__" }, settings.ActionMenuGroupOrder);
    }
}
