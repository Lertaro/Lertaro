using Lertaro.Core;
using Lertaro.App.Services.Plugin;
using Lertaro.App.ViewModels.Settings.General;
using Lertaro.PluginSdk.Abstractions.Plugins;

namespace Lertaro.App.Tests.ViewModels.Settings.General;

[TestClass]
public sealed class SidebarGroupOrderViewModelTests
{
    private sealed class FakeSidebarFilterProvider : ISidebarFilterProvider
    {
        public IEnumerable<SidebarFilterGroup> GetFilterGroups() => Enumerable.Empty<SidebarFilterGroup>();
    }

    // Regression test for the bug where the saved sidebar order silently had no effect: BuildId must
    // read the RAW plugin-defined provider's type/assembly, matching what
    // PluginManager.SidebarFilterProviders' own ordering computes -- not the FilteredSidebarFilterProvider
    // wrapper's, which would always produce a different id (its own type is "FilteredSidebarFilterProvider"
    // regardless of which plugin it wraps), so a saved order id could never match anything and every
    // provider silently fell back to its default SortOrder.
    [TestMethod]
    public void BuildId_OnWrappedProvider_MatchesRawProviderId()
    {
        var raw = new FakeSidebarFilterProvider();
        var wrapped = new FilteredSidebarFilterProvider(raw, "test.dll", PluginManager.Instance);

        var rawId = SidebarGroupOrderViewModel.BuildId(raw);
        var wrappedWithoutUnwrapping = SidebarGroupOrderViewModel.BuildId(wrapped);
        var wrappedWithUnwrapping = SidebarGroupOrderViewModel.BuildId(wrapped.Inner);

        Assert.AreNotEqual(rawId, wrappedWithoutUnwrapping, "the bug: the wrapper's own type name leaks into the id");
        Assert.AreEqual(rawId, wrappedWithUnwrapping, "the fix: unwrapping recovers the raw provider's id");
    }

    // The constructor's own enumeration of PluginManager.Instance.SidebarFilterProviders isn't exercised
    // here (no seam, matching this codebase's existing PluginManager-registry-untested convention) --
    // these tests instead seed Items directly to cover MoveUp/MoveDown/Save in isolation.
    private static SidebarGroupOrderViewModel MakeViewModel(UserSettings settings, params (string Id, string Name)[] items)
    {
        var vm = new SidebarGroupOrderViewModel(settings);
        vm.Items.Clear();
        foreach (var (id, name) in items)
            vm.Items.Add(new SidebarGroupOrderItem(id, () => name));
        return vm;
    }

    [TestMethod]
    public void MoveUpCommand_OnFirstItem_DoesNotMove()
    {
        var vm = MakeViewModel(new UserSettings(), ("type", "Type"), ("date", "Date"));

        vm.MoveUpCommand.Execute(vm.Items[0]);

        CollectionAssert.AreEqual(new[] { "type", "date" }, vm.Items.Select(x => x.Id).ToList());
    }

    [TestMethod]
    public void MoveUpCommand_OnSecondItem_SwapsWithFirst()
    {
        var vm = MakeViewModel(new UserSettings(), ("type", "Type"), ("date", "Date"));

        vm.MoveUpCommand.Execute(vm.Items[1]);

        CollectionAssert.AreEqual(new[] { "date", "type" }, vm.Items.Select(x => x.Id).ToList());
    }

    [TestMethod]
    public void MoveDownCommand_OnLastItem_DoesNotMove()
    {
        var vm = MakeViewModel(new UserSettings(), ("type", "Type"), ("date", "Date"));

        vm.MoveDownCommand.Execute(vm.Items[1]);

        CollectionAssert.AreEqual(new[] { "type", "date" }, vm.Items.Select(x => x.Id).ToList());
    }

    [TestMethod]
    public void MoveDownCommand_OnFirstItem_SwapsWithSecond()
    {
        var vm = MakeViewModel(new UserSettings(), ("type", "Type"), ("date", "Date"));

        vm.MoveDownCommand.Execute(vm.Items[0]);

        CollectionAssert.AreEqual(new[] { "date", "type" }, vm.Items.Select(x => x.Id).ToList());
    }

    [TestMethod]
    public void Save_WritesItemIdsInOrderToUserSettings()
    {
        var settings = new UserSettings();
        var vm = MakeViewModel(settings, ("date", "Date"), ("type", "Type"), ("size", "Size"));

        vm.Save();

        CollectionAssert.AreEqual(new[] { "date", "type", "size" }, settings.SidebarGroupOrder);
    }
}
