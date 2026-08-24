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

[TestClass]
public sealed class PluginInfoViewModelTests
{
    private static PluginComponentViewModel Component(string id, PluginComponentType type, bool enabled = true) => new(id, type, id, enabled);

    private static PluginInfoViewModel MakeVm(List<PluginComponentViewModel> components, List<PluginConfigFieldViewModel>? configFields = null) =>
        new("Name", "1.0", "plugin.dll", "1.0-sdk", components, configFields ?? new List<PluginConfigFieldViewModel>());

    [TestMethod]
    public void Constructor_SetsBasicFields()
    {
        var vm = MakeVm(new List<PluginComponentViewModel>());

        Assert.AreEqual("Name", vm.Name);
        Assert.AreEqual("1.0", vm.Version);
        Assert.AreEqual("plugin.dll", vm.DllFileName);
        Assert.AreEqual("1.0-sdk", vm.SdkVersion);
    }

    [TestMethod]
    public void Constructor_GroupsComponentsByType()
    {
        var vm = MakeVm(new List<PluginComponentViewModel>
        {
            Component("a1", PluginComponentType.Action),
            Component("a2", PluginComponentType.Action),
            Component("f1", PluginComponentType.FilterProvider),
        });

        Assert.HasCount(2, vm.ComponentGroups);
        Assert.HasCount(2, vm.ComponentGroups.Single(g => g.ComponentType == PluginComponentType.Action).Components);
    }

    [TestMethod]
    public void HasNoComponents_EmptyComponentList_ReturnsTrue() =>
        Assert.IsTrue(MakeVm(new List<PluginComponentViewModel>()).HasNoComponents);

    [TestMethod]
    public void HasConfigFields_NonEmptyList_ReturnsTrue()
    {
        var field = new PluginConfigFieldViewModel(
            "plugin",
            new PluginSdk.Abstractions.PluginConfigField { Key = "k", FieldType = PluginSdk.Abstractions.ConfigFieldType.Text, DefaultValue = "" },
            new Core.UserSettings(),
            () => { });

        var vm = MakeVm(new List<PluginComponentViewModel>(), new List<PluginConfigFieldViewModel> { field });

        Assert.IsTrue(vm.HasConfigFields);
    }

    [TestMethod]
    public void ToggleAllComponentsCommand_TogglesEveryToggleableComponentAcrossGroups()
    {
        var vm = MakeVm(new List<PluginComponentViewModel>
        {
            Component("a1", PluginComponentType.Action, enabled: true),
            Component("f1", PluginComponentType.FilterProvider, enabled: true),
        });

        vm.ToggleAllComponentsCommand.Execute(null);

        Assert.IsTrue(vm.RawComponents.All(c => !c.IsEnabled));
    }

    // IsExpanded went with the card column it belonged to: the page now shows one plugin at a time, so
    // "which plugin am I looking at" is the list's selection rather than a flag on every plugin. What
    // replaced it is IsConfigOpen, which is about the config form inside that one plugin's pane.
    [TestMethod]
    public void TheDetailsTabIsTheOneShownFirst() =>
        // Selecting a plugin should show what it is and what it provides, not drop the user straight
        // into a form.
        Assert.IsFalse(MakeVm(new List<PluginComponentViewModel>()).IsConfigTab);

    [TestMethod]
    public void TheTabCommandsMoveBetweenTheTwoTabs()
    {
        var vm = MakeVm(new List<PluginComponentViewModel>());

        vm.ShowConfigCommand.Execute(null);
        Assert.IsTrue(vm.IsConfigTab);

        vm.ShowDetailsCommand.Execute(null);
        Assert.IsFalse(vm.IsConfigTab);
    }

    [TestMethod]
    public void WebsiteProperties_WhenSet_ReturnsExpectedValues()
    {
        var vm = new PluginInfoViewModel(
            "Flow", "1.0", "flow.dll", "1.0-sdk",
            new List<PluginComponentViewModel>(),
            new List<PluginConfigFieldViewModel>(),
            websiteUrl: "https://www.flowlauncher.com/plugins",
            websiteLabel: "Browse Plugins");

        Assert.IsTrue(vm.HasWebsite);
        Assert.AreEqual("https://www.flowlauncher.com/plugins", vm.WebsiteUrl);
        Assert.AreEqual("Browse Plugins", vm.WebsiteLabel);
        Assert.AreEqual("Browse Plugins", vm.DisplayWebsiteLabel);
    }

    [TestMethod]
    public void HasWebsite_WhenNullOrEmpty_ReturnsFalse()
    {
        var vm = new PluginInfoViewModel(
            "Test", "1.0", "test.dll", "1.0-sdk",
            new List<PluginComponentViewModel>(),
            new List<PluginConfigFieldViewModel>());

        Assert.IsFalse(vm.HasWebsite);
    }
}
