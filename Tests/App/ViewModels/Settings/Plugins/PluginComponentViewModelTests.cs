using Lertaro.App.ViewModels.Settings.Plugins;

namespace Lertaro.App.Tests.ViewModels.Settings.Plugins;

[TestClass]
public sealed class PluginComponentViewModelTests
{
    [TestMethod]
    public void Constructor_SetsAllProvidedFields()
    {
        var vm = new PluginComponentViewModel("id1", PluginComponentType.Action, "My Action", isEnabled: true, "desc");

        Assert.AreEqual("id1", vm.ComponentId);
        Assert.AreEqual(PluginComponentType.Action, vm.ComponentType);
        Assert.AreEqual("My Action", vm.DisplayName);
        Assert.IsTrue(vm.IsEnabled);
        Assert.AreEqual("desc", vm.Description);
    }

    [TestMethod]
    public void IsToggleable_TranslationProvider_ReturnsFalse() =>
        Assert.IsFalse(new PluginComponentViewModel("id", PluginComponentType.TranslationProvider, "n", true).IsToggleable);

    [TestMethod]
    public void IsToggleable_ThemeProvider_ReturnsFalse() =>
        Assert.IsFalse(new PluginComponentViewModel("id", PluginComponentType.ThemeProvider, "n", true).IsToggleable);

    [TestMethod]
    public void IsToggleable_OrdinaryComponent_ReturnsTrue() =>
        Assert.IsTrue(new PluginComponentViewModel("id", PluginComponentType.Action, "n", true).IsToggleable);

    [TestMethod]
    public void IsDirty_DefaultsToFalse() =>
        Assert.IsFalse(new PluginComponentViewModel("id", PluginComponentType.Action, "n", true).IsDirty);

    [TestMethod]
    public void IsEnabled_Set_MarksDirty()
    {
        var vm = new PluginComponentViewModel("id", PluginComponentType.Action, "n", isEnabled: true);

        vm.IsEnabled = false;

        Assert.IsTrue(vm.IsDirty);
    }

    [TestMethod]
    public void IsEnabled_SetToSameValue_DoesNotMarkDirty()
    {
        var vm = new PluginComponentViewModel("id", PluginComponentType.Action, "n", isEnabled: true);

        vm.IsEnabled = true;

        Assert.IsFalse(vm.IsDirty);
    }
}
