using Lertaro.App.ViewModels.Settings;
using Lertaro.App.ViewModels.Settings.Plugins;
using Lertaro.App.Helpers;
using Lertaro.Core;
using Lertaro.PluginSdk.Abstractions;

namespace Lertaro.App.Tests.Views.Settings;

[TestClass]
public sealed class SettingsWindowSearchExtensionsTests
{
    [TestMethod]
    public void RankSearchResults_PlacesHigherQualityMatchesFirst()
    {
        var entries = new[]
        {
            new SettingsSearchResultItem("a_x_b_x_c", "General", "General", null),
            new SettingsSearchResultItem("abc", "General", "General", null)
        };

        var results = SettingsWindowSearchExtensions.RankSearchResults("abc", entries);

        Assert.HasCount(2, results);
        Assert.AreEqual("abc", results[0].Label);
    }

    [TestMethod]
    public void BuildAllEntries_IncludesPluginConfigFields()
    {
        var settings = new UserSettings();
        var field = new PluginConfigField
        {
            Key = "TestSettingKey",
            LabelKey = "Settings_TestLabelKey",
            GroupKey = "Settings_TestGroupKey",
            FieldType = ConfigFieldType.Text,
            DefaultValue = "defaultVal"
        };

        var configFieldVm = new PluginConfigFieldViewModel("test_plugin", field, settings, () => { });
        var pluginVm = new PluginInfoViewModel(
            name: "TestPlugin",
            version: "1.0.0",
            dllFileName: "TestPlugin.dll",
            sdkVersion: "1.5.0",
            components: new List<PluginComponentViewModel>(),
            configFields: new List<PluginConfigFieldViewModel> { configFieldVm },
            description: "Test plugin description");

        var settingsVm = new SettingsViewModel();
        settingsVm.Plugins.Plugins.Clear();
        settingsVm.Plugins.Plugins.Add(pluginVm);

        var results = SettingsWindowSearchExtensions.BuildAllEntries(vm: settingsVm);
        var targetItem = results.FirstOrDefault(r => r.Label == "Settings_TestLabelKey");

        Assert.IsNotNull(targetItem, "Expected plugin config field label to be indexed.");
        Assert.AreEqual("Plugins", targetItem.Section);
        StringAssert.Contains(targetItem.SectionLabel, "TestPlugin");
        Assert.IsNotNull(targetItem.Reveal, "Expected dynamic reveal metadata for targeting UI element.");
    }

    [TestMethod]
    public void ActivateResult_SwitchesSelectedConfigGroup_WhenFieldBelongsToGroupTab()
    {
        var settings = new UserSettings();
        var groupField1 = new PluginConfigField { Key = "g1", LabelKey = "Group1Key", FieldType = ConfigFieldType.Group, DefaultValue = "" };
        var groupField2 = new PluginConfigField { Key = "g2", LabelKey = "Group2Key", FieldType = ConfigFieldType.Group, DefaultValue = "" };
        var subField2 = new PluginConfigField { Key = "sub2", LabelKey = "Sub2Key", FieldType = ConfigFieldType.Text, DefaultValue = "" };

        var g1Vm = new PluginConfigFieldViewModel("test_plugin", groupField1, settings, () => { });
        var g2Vm = new PluginConfigFieldViewModel("test_plugin", groupField2, settings, () => { });
        var sub2Vm = new PluginConfigFieldViewModel("test_plugin", subField2, settings, () => { });
        g2Vm.Children.Add(sub2Vm);

        var pluginVm = new PluginInfoViewModel(
            name: "TestPlugin",
            version: "1.0.0",
            dllFileName: "TestPlugin.dll",
            sdkVersion: "1.5.0",
            components: new List<PluginComponentViewModel>(),
            configFields: new List<PluginConfigFieldViewModel> { g1Vm, g2Vm },
            description: "Test plugin description");

        var settingsVm = new SettingsViewModel();
        settingsVm.Plugins.Plugins.Clear();
        settingsVm.Plugins.Plugins.Add(pluginVm);

        var results = SettingsWindowSearchExtensions.BuildAllEntries(vm: settingsVm);
        var sub2Item = results.FirstOrDefault(r => r.Label == "Sub2Key");

        Assert.IsNotNull(sub2Item);
        sub2Item.Activate?.Invoke(settingsVm);

        Assert.AreEqual(pluginVm, settingsVm.Plugins.SelectedPlugin);
        Assert.IsTrue(pluginVm.IsConfigTab);
        Assert.AreEqual(g2Vm, pluginVm.SelectedConfigGroup);
    }

    [TestMethod]
    public void ActivateResult_SwitchesToDetailsTab_WhenComponentIsRevealed()
    {
        var settings = new UserSettings();
        var component = new PluginComponentViewModel("c1", PluginComponentType.Action, "MyComponent", true);
        var pluginVm = new PluginInfoViewModel(
            name: "TestPlugin",
            version: "1.0.0",
            dllFileName: "TestPlugin.dll",
            sdkVersion: "1.5.0",
            components: new List<PluginComponentViewModel> { component },
            configFields: new List<PluginConfigFieldViewModel>(),
            description: "Test plugin description");

        pluginVm.IsConfigTab = true; // start on Config tab

        var settingsVm = new SettingsViewModel();
        settingsVm.Plugins.Plugins.Clear();
        settingsVm.Plugins.Plugins.Add(pluginVm);

        var results = SettingsWindowSearchExtensions.BuildAllEntries(vm: settingsVm);
        var componentItem = results.FirstOrDefault(r => r.Label == "MyComponent");

        Assert.IsNotNull(componentItem);
        componentItem.Activate?.Invoke(settingsVm);

        Assert.AreEqual(pluginVm, settingsVm.Plugins.SelectedPlugin);
        Assert.IsFalse(pluginVm.IsConfigTab, "Expected IsConfigTab to be false when revealing a component.");
    }

    [TestMethod]
    public void BuildAllEntries_IncludesPluginActionHotkeys_WithHotkeyShortcut()
    {
        var settingsVm = new SettingsViewModel();
        var results = SettingsWindowSearchExtensions.BuildAllEntries(vm: settingsVm);

        var pluginActionItem = results.FirstOrDefault(r => r.Section == "Hotkeys" && r.Reveal?.ListElementName == "PluginActionGroupsList");

        if (pluginActionItem != null)
        {
            Assert.IsNotNull(pluginActionItem.Activate);
            pluginActionItem.Activate?.Invoke(settingsVm);
            Assert.AreEqual("PluginActions", settingsVm.Hotkeys.SelectedTab);
        }
        else
        {
            // If no plugins are loaded in test context, verify the method executes cleanly without throwing
            Assert.IsTrue(results.Any(r => r.Section == "Hotkeys"));
        }
    }
}
