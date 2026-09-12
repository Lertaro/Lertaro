using Lertaro.Core;
using Lertaro.PluginSdk.Abstractions;
using Lertaro.App.ViewModels.Settings.Plugins;

namespace Lertaro.App.Tests.ViewModels.Settings.Plugins;

// The bug this pins: editing a plugin's config, switching to another plugin without pressing Apply or
// OK, and coming back used to show the edited values gone -- the switch rolled them back. Edits are now
// staged per plugin and kept until the Settings window's Apply/OK (or discarded on Cancel), so these
// assert that a staged edit survives the switch and is still what gets committed.
[TestClass]
public sealed class PluginConfigStagedEditTests
{
    private static PluginConfigField TextField(string key = "k", string defaultValue = "default") => new()
    {
        Key = key,
        FieldType = ConfigFieldType.Text,
        DefaultValue = defaultValue,
    };

    private static PluginInfoViewModel Plugin(string name, List<PluginConfigFieldViewModel> fields, Action? onSave = null) =>
        new(name, "1.0", name + ".dll", "1.0-sdk", [], fields, onSave: onSave);

    private static PluginConfigFieldViewModel Vm(PluginConfigField field, UserSettings settings, Action? onValueChanged = null) =>
        new("plugin", field, settings, onValueChanged);

    [TestMethod]
    public void ValueEdit_MarksTheFieldDirty()
    {
        var vm = Vm(TextField(), new UserSettings());

        Assert.IsFalse(vm.IsDirty, "a freshly loaded field is clean");

        vm.Value = "typed";

        Assert.IsTrue(vm.IsDirty);
    }

    [TestMethod]
    public void ValueEdit_SurvivesLeavingAndReopeningTheConfigTab()
    {
        // The exact user-visible fix: type something, switch tabs (which drops the rows of clean fields
        // but must keep a dirty one), come back, and the typed value is still there.
        var settings = new UserSettings();
        var vm = Vm(TextField(), settings);
        var plugin = Plugin("P", [vm]);

        plugin.IsConfigTab = true;
        vm.Value = "typed";
        plugin.IsConfigTab = false;
        plugin.IsConfigTab = true;

        Assert.AreEqual("typed", vm.Value);
        Assert.IsTrue(plugin.HasPendingConfigEdits);
    }

    [TestMethod]
    public void GroupChildEdit_IsVisibleThroughThePluginAndSurvivesTheSwitch()
    {
        // A group is a layout container: the edit is on its child, but the plugin's pending state has to
        // see through to it or Apply would skip the plugin entirely.
        var settings = new UserSettings();
        var group = Vm(new PluginConfigField
        {
            Key = "group",
            FieldType = ConfigFieldType.Group,
            SubFields = [TextField("child", "fallback")],
        }, settings);
        var plugin = Plugin("P", [group]);

        plugin.IsConfigTab = true;
        group.Children[0].Value = "edited";
        Assert.IsTrue(plugin.HasPendingConfigEdits);

        plugin.IsConfigTab = false;
        plugin.IsConfigTab = true;

        Assert.AreEqual("edited", group.Children[0].Value);
        Assert.IsTrue(plugin.HasPendingConfigEdits);
    }

    [TestMethod]
    public void ArrayStructuralChange_MarksTheFieldDirty()
    {
        // Add/Delete/Move/Duplicate touch ArrayItems, and a drag-reorder mutates the bound collection
        // directly -- all of them must count, or a user who only reorders rows loses that on Apply.
        var settings = new UserSettings();
        var vm = Vm(new PluginConfigField
        {
            Key = "items",
            FieldType = ConfigFieldType.Array,
            SubFields = [new PluginConfigField { Key = "value", FieldType = ConfigFieldType.Text, DefaultValue = "" }],
            DefaultValue = new List<object>(),
        }, settings);

        Assert.IsFalse(vm.IsDirty, "loading the (empty) rows is not an edit");

        vm.AddCommand.Execute(null);

        Assert.IsTrue(vm.IsDirty);
    }

    [TestMethod]
    public void ArrayRowEdit_MarksTheFieldDirty()
    {
        var settings = new UserSettings();
        var vm = Vm(new PluginConfigField
        {
            Key = "items",
            FieldType = ConfigFieldType.Array,
            SubFields = [new PluginConfigField { Key = "value", FieldType = ConfigFieldType.Text, DefaultValue = "" }],
            DefaultValue = new List<object> { new Dictionary<string, object> { ["value"] = "row" } },
        }, settings);

        Assert.IsFalse(vm.IsDirty, "the loaded row is clean");

        vm.ArrayItems[0].Children[0].Value = "edited";

        Assert.IsTrue(vm.IsDirty);
    }

    [TestMethod]
    public void ValueEdit_SurvivesARealPluginSwitchAndReopen()
    {
        // The reported bug, end to end through the two hooks the list actually calls: the selection
        // setter releases the outgoing plugin's rows (CloseConfigRowsForSelectionChange) and the config
        // tab re-materializes them on the next open (IsConfigTab = true). The typed value must still be
        // there -- that is the whole point.
        var settings = new UserSettings();
        var vm = Vm(TextField(), settings);
        var plugin = Plugin("P", [vm]);

        plugin.IsConfigTab = true;
        vm.Value = "typed";

        // Switch away to another plugin, then come back and open the config again.
        plugin.CloseConfigRowsForSelectionChange();
        plugin.IsConfigTab = true;

        Assert.AreEqual("typed", vm.Value);
        Assert.IsTrue(plugin.HasPendingConfigEdits);
    }

    [TestMethod]
    public void CleanPluginRows_AreStillReleasedOnSwitch()
    {
        // The other half: a plugin the user only looked at (no edit) must still give its rows back, or
        // the page would hold every visited plugin's whole schema tree.
        var settings = new UserSettings();
        var group = Vm(new PluginConfigField
        {
            Key = "group",
            FieldType = ConfigFieldType.Group,
            SubFields = [TextField("child", "fallback")],
        }, settings);
        var plugin = Plugin("P", [group]);

        plugin.IsConfigTab = true;
        _ = group.Children;
        Assert.IsTrue(group.HasLoadedChildren, "precondition: the rows were materialized");

        plugin.CloseConfigRowsForSelectionChange();

        Assert.IsFalse(group.HasLoadedChildren, "a clean plugin's rows are dropped on switch");
    }

    [TestMethod]
    public void DiscardStagedEdits_RevertsEverythingTheUserTyped()
    {
        // Cancel's path. Whatever survived the switches above must be gone here, so a later OK cannot
        // write values the user thought they had cancelled.
        var settings = new UserSettings();
        settings.SetPluginSetting("plugin", "k", "persisted");
        var vm = Vm(TextField(defaultValue: "persisted"), settings);
        var plugin = Plugin("P", [vm]);

        plugin.IsConfigTab = true;
        vm.Value = "typed";
        plugin.IsConfigTab = false;

        plugin.RollbackConfig();

        Assert.IsFalse(plugin.HasPendingConfigEdits, "Cancel must clear the staged flag");
        Assert.AreEqual("persisted", vm.Value, "and restore the persisted value");
    }

    [TestMethod]
    public void Commit_ClearsThePendingStateSoOnlyRealEditsAreSaved()
    {
        var settings = new UserSettings();
        var vm = Vm(TextField(), settings);
        var plugin = Plugin("P", [vm]);
        vm.Value = "typed";

        vm.Commit();

        Assert.IsFalse(plugin.HasPendingConfigEdits);
        Assert.AreEqual("typed", settings.GetPluginSetting<string?>("plugin", "k", null));
    }
}
