using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.App.ViewModels.Settings;

namespace Lertaro.App.Tests.ViewModels.Settings;

[TestClass]
public sealed class PluginActionHotkeyItemViewModelTests
{
    private sealed class FakeAction : ISearchResultAction
    {
        public string GroupName => "Group";
        public string DisplayName { get; init; } = "Copy";
        public string Hotkey => "Ctrl+C";
        public System.Windows.Media.ImageSource? Icon => null;
        public bool CanExecute(IReadOnlyList<ISearchResult> results) => true;
        public void Execute(IReadOnlyList<ISearchResult> results, IPluginSearchWindow view) { }
    }

    private sealed class FakePlugin : IPlugin
    {
        public string Name { get; init; } = "Test Plugin";
    }

    [TestMethod]
    public void Constructor_SetsPluginIdActionIdDefaultHotkeyAndCurrentValue()
    {
        var vm = new PluginActionHotkeyItemViewModel("myplugin", new FakeAction(), "Ctrl+Shift+C");

        Assert.AreEqual("myplugin", vm.PluginId);
        Assert.AreEqual(nameof(FakeAction), vm.ActionId);
        Assert.AreEqual("Ctrl+C", vm.DefaultHotkey);
        Assert.AreEqual("Ctrl+Shift+C", vm.HotkeyValue);
    }

    [TestMethod]
    public void DisplayName_ReflectsActionsDisplayNameLive()
    {
        var action = new FakeAction { DisplayName = "Copy Path" };
        var vm = new PluginActionHotkeyItemViewModel("myplugin", action, "");

        Assert.AreEqual("Copy Path", vm.DisplayName);
    }

    [TestMethod]
    public void RefreshDisplayName_RaisesPropertyChangedForDisplayName()
    {
        var vm = new PluginActionHotkeyItemViewModel("myplugin", new FakeAction(), "");
        var raised = false;
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.DisplayName)) raised = true; };

        vm.RefreshDisplayName();

        Assert.IsTrue(raised);
    }

    [TestMethod]
    public void HotkeyValue_Set_RaisesPropertyChanged()
    {
        var vm = new PluginActionHotkeyItemViewModel("myplugin", new FakeAction(), "");
        var raised = false;
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.HotkeyValue)) raised = true; };

        vm.HotkeyValue = "Ctrl+D";

        Assert.IsTrue(raised);
        Assert.AreEqual("Ctrl+D", vm.HotkeyValue);
    }

    [TestMethod]
    public void PluginActionGroupViewModel_PluginName_ReflectsPluginNameLive()
    {
        var group = new PluginActionGroupViewModel(new FakePlugin { Name = "My Plugin" }, new List<PluginActionHotkeyItemViewModel>());

        Assert.AreEqual("My Plugin", group.PluginName);
    }

    [TestMethod]
    public void PluginActionGroupViewModel_Items_ReturnsProvidedList()
    {
        var items = new List<PluginActionHotkeyItemViewModel> { new("p", new FakeAction(), "") };

        var group = new PluginActionGroupViewModel(new FakePlugin(), items);

        Assert.AreSame(items, group.Items);
    }

    [TestMethod]
    public void PluginActionGroupViewModel_RefreshPluginName_RaisesPropertyChanged()
    {
        var group = new PluginActionGroupViewModel(new FakePlugin(), new List<PluginActionHotkeyItemViewModel>());
        var raised = false;
        group.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(group.PluginName)) raised = true; };

        group.RefreshPluginName();

        Assert.IsTrue(raised);
    }
}
