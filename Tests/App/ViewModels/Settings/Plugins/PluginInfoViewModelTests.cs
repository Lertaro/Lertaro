using Lertaro.App.ViewModels.Settings.Plugins;

namespace Lertaro.App.Tests.ViewModels.Settings.Plugins;

[TestClass]
public sealed class PluginInfoViewModelTests
{
    private static PluginComponentViewModel Component(string id, PluginComponentType type, bool enabled = true) => new(id, type, id, enabled);

    private static PluginInfoViewModel MakeVm(
        List<PluginComponentViewModel> components,
        List<PluginConfigFieldViewModel>? configFields = null,
        Action? onRollback = null) =>
        new("Name", "1.0", "plugin.dll", "1.0-sdk", components, configFields ?? new List<PluginConfigFieldViewModel>(),
            onRollback: onRollback);

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

    // RollbackConfig discards staged edits and rebuilds every field from settings, which is real work
    // proportional to the schema. It is only meaningful once the config tab has actually been opened --
    // edits are only possible while it was, so a never-opened plugin has nothing to discard. The
    // OnRollback callback is the observable edge of that work, so these count it.
    [TestMethod]
    public void RollbackWithoutTheConfigTabEverBeingOpened_IsANoOp()
    {
        var rollbacks = 0;
        var vm = MakeVm(new List<PluginComponentViewModel>(), [TextField()], onRollback: () => rollbacks++);

        vm.RollbackConfig();

        Assert.AreEqual(0, rollbacks, "a never-opened config tab has nothing staged to roll back");
    }

    [TestMethod]
    public void RollbackAfterTheConfigTabWasOpened_DoesTheWorkOnce()
    {
        var rollbacks = 0;
        var vm = MakeVm(new List<PluginComponentViewModel>(), [TextField()], onRollback: () => rollbacks++);

        vm.IsConfigTab = true;
        vm.RollbackConfig();

        Assert.AreEqual(1, rollbacks);
    }

    // Leaving the config tab (or switching to another plugin) must NOT discard staged edits: the
    // Settings window's Apply/OK is the one commit point, so a user who steps away to check something
    // and comes back has to still find what they typed. Only Cancel discards, through RollbackConfig.
    [TestMethod]
    public void LeavingTheConfigTab_KeepsStagedEdits()
    {
        var rollbacks = 0;
        var vm = MakeVm(new List<PluginComponentViewModel>(), [TextField()], onRollback: () => rollbacks++);

        vm.IsConfigTab = true;
        vm.IsConfigTab = false;

        Assert.AreEqual(0, rollbacks, "navigating away must not roll staged edits back");
    }

    [TestMethod]
    public void SelectingAnotherPlugin_DoesNotDiscardStagedEdits()
    {
        // The selection setter used to call RollbackConfig, which is exactly what lost a user's edits
        // when they glanced at another plugin. It still releases the rebuildable rows, but must leave
        // any actually-edited tree (and therefore the edit) alone.
        var rollbacks = 0;
        var vm = MakeVm(new List<PluginComponentViewModel>(), [TextField()], onRollback: () => rollbacks++);

        vm.IsConfigTab = true;
        vm.CloseConfigRowsForSelectionChange();

        Assert.AreEqual(0, rollbacks);
    }

    [TestMethod]
    public void HasPendingConfigEdits_AfterEditingAField_IsTrue()
    {
        var vm = MakeVm(new List<PluginComponentViewModel>(), [TextField()]);

        Assert.IsFalse(vm.HasPendingConfigEdits, "a freshly loaded plugin has nothing staged");

        vm.ConfigFields[0].Value = "typed";

        Assert.IsTrue(vm.HasPendingConfigEdits);
    }

    [TestMethod]
    public void HasPendingConfigEdits_AfterCommit_IsFalse()
    {
        var vm = MakeVm(new List<PluginComponentViewModel>(), [TextField()]);
        vm.ConfigFields[0].Value = "typed";

        vm.ConfigFields[0].Commit();

        Assert.IsFalse(vm.HasPendingConfigEdits, "a committed plugin has nothing left to save");
    }

    private static PluginConfigFieldViewModel TextField() =>
        new("p", new PluginSdk.Abstractions.PluginConfigField
        {
            Key = "k",
            FieldType = PluginSdk.Abstractions.ConfigFieldType.Text,
            DefaultValue = "default"
        }, new Core.UserSettings());

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

    [TestMethod]
    public void ConfigGroupProperties_MultipleGroups_PopulatesActiveChildrenAndNullsFlatFields()
    {
        var g1 = new PluginConfigFieldViewModel("p", new PluginSdk.Abstractions.PluginConfigField { Key = "g1", FieldType = PluginSdk.Abstractions.ConfigFieldType.Group, SubFields = [new PluginSdk.Abstractions.PluginConfigField { Key = "f1", FieldType = PluginSdk.Abstractions.ConfigFieldType.Text, DefaultValue = "" }] }, new Core.UserSettings(), null);
        var g2 = new PluginConfigFieldViewModel("p", new PluginSdk.Abstractions.PluginConfigField { Key = "g2", FieldType = PluginSdk.Abstractions.ConfigFieldType.Group, SubFields = [new PluginSdk.Abstractions.PluginConfigField { Key = "f2", FieldType = PluginSdk.Abstractions.ConfigFieldType.Text, DefaultValue = "" }] }, new Core.UserSettings(), null);

        var vm = MakeVm([], [g1, g2]);

        Assert.IsTrue(vm.HasMultipleConfigGroups);
        Assert.IsNotNull(vm.ActiveConfigGroupChildren);
        Assert.IsNull(vm.FlatConfigFields);
        Assert.AreEqual(g1, vm.SelectedConfigGroup);

        vm.SelectConfigGroupCommand.Execute(g2);
        Assert.AreEqual(g2, vm.SelectedConfigGroup);
    }

    [TestMethod]
    public void ConfigGroupProperties_SingleOrNoGroup_PopulatesFlatFieldsAndNullsActiveChildren()
    {
        var g1 = new PluginConfigFieldViewModel("p", new PluginSdk.Abstractions.PluginConfigField { Key = "g1", FieldType = PluginSdk.Abstractions.ConfigFieldType.Group, SubFields = [] }, new Core.UserSettings(), null);
        var vm = MakeVm([], [g1]);

        Assert.IsFalse(vm.HasMultipleConfigGroups);
        Assert.IsNull(vm.ActiveConfigGroupChildren);
        Assert.IsNotNull(vm.FlatConfigFields);
        Assert.HasCount(1, vm.FlatConfigFields);
    }

    [TestMethod]
    public void IsFullyDisabled_AllToggleableComponentsStartDisabled_True()
    {
        var vm = MakeVm(new List<PluginComponentViewModel>
        {
            Component("a1", PluginComponentType.Action, enabled: false),
            Component("f1", PluginComponentType.FilterProvider, enabled: false),
        });

        Assert.IsTrue(vm.IsFullyDisabled);
    }

    [TestMethod]
    public void IsFullyDisabled_SomeComponentStillEnabled_False()
    {
        var vm = MakeVm(new List<PluginComponentViewModel>
        {
            Component("a1", PluginComponentType.Action, enabled: false),
            Component("f1", PluginComponentType.FilterProvider, enabled: true),
        });

        Assert.IsFalse(vm.IsFullyDisabled);
    }

    [TestMethod]
    public void IsFullyDisabled_NoToggleableComponents_False()
    {
        // Translation/theme-only plugins have nothing the user can turn off, so they can
        // never count as fully disabled.
        var vm = MakeVm(new List<PluginComponentViewModel>
        {
            Component("t1", PluginComponentType.TranslationProvider, enabled: true),
        });

        Assert.IsFalse(vm.IsFullyDisabled);
    }

    [TestMethod]
    public void IsFullyDisabled_TogglingLastComponentOn_FlipsToFalse()
    {
        var a1 = Component("a1", PluginComponentType.Action, enabled: false);
        var vm = MakeVm(new List<PluginComponentViewModel> { a1 });

        a1.IsEnabled = true;

        Assert.IsFalse(vm.IsFullyDisabled);
    }

    [TestMethod]
    public void IsFullyDisabled_TogglingLastComponentOff_FlipsToTrue()
    {
        var a1 = Component("a1", PluginComponentType.Action, enabled: true);
        var vm = MakeVm(new List<PluginComponentViewModel> { a1 });

        a1.IsEnabled = false;

        Assert.IsTrue(vm.IsFullyDisabled);
    }
}
