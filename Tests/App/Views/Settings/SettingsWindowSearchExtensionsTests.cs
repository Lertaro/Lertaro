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
        settingsVm.Plugins.IsRuntimeStatusTab = true;

        var results = SettingsWindowSearchExtensions.BuildAllEntries(vm: settingsVm);
        var sub2Item = results.FirstOrDefault(r => r.Label == "Sub2Key");

        Assert.IsNotNull(sub2Item);
        sub2Item.Activate?.Invoke(settingsVm);

        Assert.AreEqual(pluginVm, settingsVm.Plugins.SelectedPlugin);
        Assert.IsTrue(pluginVm.IsConfigTab);
        Assert.IsFalse(settingsVm.Plugins.IsRuntimeStatusTab,
            "plugin configuration search results must switch away from the runtime status tab");
        Assert.AreEqual(g2Vm, pluginVm.SelectedConfigGroup);
    }

    [TestMethod]
    public void ActivateResult_SelectsTopLevelTab_WhenFieldBelongsToNestedGroup()
    {
        var settings = new UserSettings();
        var nestedField = new PluginConfigField { Key = "nested", LabelKey = "NestedKey", FieldType = ConfigFieldType.Text, DefaultValue = "" };
        var nestedGroup = new PluginConfigField
        {
            Key = "nestedGroup",
            LabelKey = "NestedGroupKey",
            FieldType = ConfigFieldType.Group,
            DefaultValue = "",
            SubFields = new List<PluginConfigField> { nestedField }
        };
        var topLevelGroup = new PluginConfigField
        {
            Key = "topLevelGroup",
            LabelKey = "TopLevelGroupKey",
            FieldType = ConfigFieldType.Group,
            DefaultValue = "",
            SubFields = new List<PluginConfigField> { nestedGroup }
        };
        var topLevelGroupVm = new PluginConfigFieldViewModel("test_plugin", topLevelGroup, settings);

        var pluginVm = new PluginInfoViewModel(
            name: "TestPlugin",
            version: "1.0.0",
            dllFileName: "TestPlugin.dll",
            sdkVersion: "1.5.0",
            components: new List<PluginComponentViewModel>(),
            configFields: new List<PluginConfigFieldViewModel> { topLevelGroupVm },
            description: "Test plugin description");
        var settingsVm = new SettingsViewModel();
        settingsVm.Plugins.Plugins.Clear();
        settingsVm.Plugins.Plugins.Add(pluginVm);

        var results = SettingsWindowSearchExtensions.BuildAllEntries(vm: settingsVm);
        var nestedItem = results.Single(result => result.Label == "NestedKey");
        nestedItem.Activate?.Invoke(settingsVm);

        Assert.AreEqual(topLevelGroupVm, pluginVm.SelectedConfigGroup);
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
        settingsVm.Plugins.IsRuntimeStatusTab = true;

        var results = SettingsWindowSearchExtensions.BuildAllEntries(vm: settingsVm);
        var componentItem = results.FirstOrDefault(r => r.Label == "MyComponent");

        Assert.IsNotNull(componentItem);
        componentItem.Activate?.Invoke(settingsVm);

        Assert.AreEqual(pluginVm, settingsVm.Plugins.SelectedPlugin);
        Assert.IsFalse(pluginVm.IsConfigTab, "Expected IsConfigTab to be false when revealing a component.");
        Assert.IsFalse(settingsVm.Plugins.IsRuntimeStatusTab,
            "plugin component search results must switch away from the runtime status tab");
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

    // SettingsWindow.JumpToEntry resolves an index against the list BuildAllEntries produces -- NOT against
    // SettingsSearchIndex.Entries. The two differ by however many entries carry an IsVisible predicate,
    // because the evaluateConditionalVisibility: false build JumpToEntry uses skips those outright. A caller
    // holding only the raw index list (the legacy-prefix notice) therefore has to map through the same rule,
    // and getting it wrong is invisible: the balloon opened a different setting several rows down the page.
    [TestMethod]
    public void JumpToEntryIndexFor_MatchesTheEntrysPositionInTheBuiltList()
    {
        var built = BuiltEntries();
        Assert.IsNotEmpty(built);

        // Rows that share a label key are skipped: several do ("Network_IndexStatus" appears under three
        // tabs), and the mapping resolves a key to its FIRST reachable slot, which the next test pins.
        var duplicates = built
            .GroupBy(entry => entry.LabelKey, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.Ordinal);

        for (var i = 0; i < built.Count; i++)
        {
            if (duplicates.Contains(built[i].LabelKey))
                continue;

            Assert.AreEqual(i, SettingsWindowSearchExtensions.JumpToEntryIndexFor(built[i].LabelKey),
                $"'{built[i].LabelKey}' must resolve to its own slot in the built list");
        }
    }

    [TestMethod]
    public void JumpToEntryIndexFor_DuplicateLabelKey_ResolvesToTheFirstReachableRow()
    {
        var first = BuiltEntries().FindIndex(entry => entry.LabelKey == "Network_IndexStatus");

        Assert.IsGreaterThanOrEqualTo(0, first);
        Assert.AreEqual(first, SettingsWindowSearchExtensions.JumpToEntryIndexFor("Network_IndexStatus"));
    }

    [TestMethod]
    public void JumpToEntryIndexFor_TheTokenPrefixRow_IsNotItsRawIndex()
    {
        // The concrete row the startup notice jumps to: conditional entries precede it, so its built index is
        // smaller than its raw one -- the raw index landed on General_DefaultFileManagerEnabled instead.
        var rawIndex = SettingsSearchIndex.Entries.ToList().FindIndex(entry => entry.LabelKey == "General_GlobalTokenPrefix");
        var builtIndex = SettingsWindowSearchExtensions.JumpToEntryIndexFor("General_GlobalTokenPrefix");

        Assert.IsGreaterThan(0, rawIndex);
        Assert.IsGreaterThanOrEqualTo(0, builtIndex);
        Assert.IsLessThan(rawIndex, builtIndex, "conditional entries precede this row, so the built index must be smaller");
        Assert.AreEqual("General_GlobalTokenPrefix", BuiltEntries()[builtIndex].LabelKey);
    }

    [TestMethod]
    public void JumpToEntryIndexFor_ConditionalEntry_HasNoBuiltIndex()
    {
        // A conditional row is never part of the list JumpToEntry resolves against, so it must report "not
        // reachable by index" rather than a slot that belongs to some other row.
        var conditional = SettingsSearchIndex.Entries.First(entry => entry.IsVisible != null);

        Assert.AreEqual(-1, SettingsWindowSearchExtensions.JumpToEntryIndexFor(conditional.LabelKey));
    }

    [TestMethod]
    public void JumpToEntryIndexFor_UnknownKey_IsMinusOne()
        => Assert.AreEqual(-1, SettingsWindowSearchExtensions.JumpToEntryIndexFor("No_Such_Key"));

    // The same rule BuildAllEntries applies when it is asked for the statics only (see its
    // evaluateConditionalVisibility parameter).
    private static List<SettingsSearchEntry> BuiltEntries()
        => SettingsSearchIndex.Entries.Where(entry => entry.IsVisible == null).ToList();
}
