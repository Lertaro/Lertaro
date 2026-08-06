using Lertaro.Core;
using Lertaro.App.ViewModels.Settings.General;

namespace Lertaro.App.Tests.ViewModels.Settings.General;

[TestClass]
public sealed class FilePreviewProviderOrderViewModelTests
{
    // The constructor's own enumeration of PluginManager.Instance.FilePreviewProviders isn't exercised
    // here (no seam, matching this codebase's existing PluginManager-registry-untested convention) --
    // these tests instead seed Items directly to cover MoveUp/MoveDown/Save in isolation.
    private static FilePreviewProviderOrderViewModel MakeViewModel(UserSettings settings, params (string Id, string Name)[] items)
    {
        var vm = new FilePreviewProviderOrderViewModel(settings);
        vm.Items.Clear();
        foreach (var (id, name) in items)
            vm.Items.Add(new FilePreviewProviderOrderItem(id, () => name));
        return vm;
    }

    [TestMethod]
    public void MoveUpCommand_OnFirstItem_DoesNotMove()
    {
        var vm = MakeViewModel(new UserSettings(), ("image", "Image"), ("text", "Text"));

        vm.MoveUpCommand.Execute(vm.Items[0]);

        CollectionAssert.AreEqual(new[] { "image", "text" }, vm.Items.Select(x => x.Id).ToList());
    }

    [TestMethod]
    public void MoveUpCommand_OnSecondItem_SwapsWithFirst()
    {
        var vm = MakeViewModel(new UserSettings(), ("image", "Image"), ("text", "Text"));

        vm.MoveUpCommand.Execute(vm.Items[1]);

        CollectionAssert.AreEqual(new[] { "text", "image" }, vm.Items.Select(x => x.Id).ToList());
    }

    [TestMethod]
    public void MoveDownCommand_OnLastItem_DoesNotMove()
    {
        var vm = MakeViewModel(new UserSettings(), ("image", "Image"), ("text", "Text"));

        vm.MoveDownCommand.Execute(vm.Items[1]);

        CollectionAssert.AreEqual(new[] { "image", "text" }, vm.Items.Select(x => x.Id).ToList());
    }

    [TestMethod]
    public void MoveDownCommand_OnFirstItem_SwapsWithSecond()
    {
        var vm = MakeViewModel(new UserSettings(), ("image", "Image"), ("text", "Text"));

        vm.MoveDownCommand.Execute(vm.Items[0]);

        CollectionAssert.AreEqual(new[] { "text", "image" }, vm.Items.Select(x => x.Id).ToList());
    }

    [TestMethod]
    public void Save_WritesItemIdsInOrderToUserSettings()
    {
        var settings = new UserSettings();
        var vm = MakeViewModel(settings, ("quicklook", "QuickLook"), ("image", "Image"), ("text", "Text"));

        vm.Save();

        CollectionAssert.AreEqual(new[] { "quicklook", "image", "text" }, settings.FilePreviewProviderOrder);
    }
}
