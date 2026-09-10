using Lertaro.App.ViewModels.Settings.Plugins;

namespace Lertaro.App.Tests.ViewModels.Settings.Plugins;

[TestClass]
public sealed class PluginComponentGroupViewModelTests
{
    private static PluginComponentViewModel Component(string id, bool enabled = true, PluginComponentType type = PluginComponentType.Action) =>
        new(id, type, id, enabled);

    [TestMethod]
    public void HasToggleableComponents_MultipleToggleable_ReturnsTrue()
    {
        var group = new PluginComponentGroupViewModel(PluginComponentType.Action, new List<PluginComponentViewModel> { Component("a"), Component("b") });

        Assert.IsTrue(group.HasToggleableComponents);
    }

    [TestMethod]
    public void HasToggleableComponents_SingleToggleable_ReturnsFalse()
    {
        var group = new PluginComponentGroupViewModel(PluginComponentType.Action, new List<PluginComponentViewModel> { Component("a") });

        Assert.IsFalse(group.HasToggleableComponents);
    }

    [TestMethod]
    public void AreAllToggleableComponentsEnabled_AllEnabled_ReturnsTrue()
    {
        var group = new PluginComponentGroupViewModel(PluginComponentType.Action, new List<PluginComponentViewModel> { Component("a", true), Component("b", true) });

        Assert.IsTrue(group.AreAllToggleableComponentsEnabled);
    }

    [TestMethod]
    public void AreAllToggleableComponentsEnabled_OneDisabled_ReturnsFalse()
    {
        var group = new PluginComponentGroupViewModel(PluginComponentType.Action, new List<PluginComponentViewModel> { Component("a", true), Component("b", false) });

        Assert.IsFalse(group.AreAllToggleableComponentsEnabled);
    }

    [TestMethod]
    public void ToggleAllCommand_AllEnabled_DisablesAll()
    {
        var group = new PluginComponentGroupViewModel(PluginComponentType.Action, new List<PluginComponentViewModel> { Component("a", true), Component("b", true) });

        group.ToggleAllCommand.Execute(null);

        Assert.IsTrue(group.Components.All(c => !c.IsEnabled));
    }

    [TestMethod]
    public void ToggleAllCommand_NotAllEnabled_EnablesAll()
    {
        var group = new PluginComponentGroupViewModel(PluginComponentType.Action, new List<PluginComponentViewModel> { Component("a", true), Component("b", false) });

        group.ToggleAllCommand.Execute(null);

        Assert.IsTrue(group.Components.All(c => c.IsEnabled));
    }

    [TestMethod]
    public void ToggleAllCommand_NonToggleableComponents_AreUnaffected()
    {
        var readOnly = Component("ro", true, PluginComponentType.TranslationProvider);
        var group = new PluginComponentGroupViewModel(PluginComponentType.TranslationProvider, new List<PluginComponentViewModel> { readOnly });

        group.ToggleAllCommand.Execute(null);

        Assert.IsTrue(readOnly.IsEnabled);
    }
}
