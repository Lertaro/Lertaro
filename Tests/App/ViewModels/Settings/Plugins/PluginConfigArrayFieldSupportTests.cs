using Lertaro.Core;
using Lertaro.PluginSdk.Abstractions;
using Lertaro.App.ViewModels.Settings.Plugins;

namespace Lertaro.App.Tests.ViewModels.Settings.Plugins;

[TestClass]
public sealed class PluginConfigArrayFieldSupportTests
{
    private static PluginConfigFieldViewModel ScalarArrayField(UserSettings settings, object defaultValue) => new(
        "plugin",
        new PluginConfigField { Key = "items", FieldType = ConfigFieldType.Array, DefaultValue = defaultValue },
        settings,
        null);

    [TestMethod]
    public void LoadArrayItems_PersistedValue_PopulatesArrayItemsFromSettings()
    {
        var settings = new UserSettings();
        settings.SetPluginSetting("plugin", "items", new List<string> { "a", "b" });

        var field = ScalarArrayField(settings, new List<string>());

        Assert.HasCount(2, field.ArrayItems);
        Assert.AreEqual("a", field.ArrayItems[0].GetValue());
    }

    [TestMethod]
    public void LoadArrayItems_NoPersistedValue_FallsBackToSchemaDefault()
    {
        var field = ScalarArrayField(new UserSettings(), new List<string> { "default-item" });

        Assert.HasCount(1, field.ArrayItems);
        Assert.AreEqual("default-item", field.ArrayItems[0].GetValue());
    }

    [TestMethod]
    public void LoadArrayItems_SelectsFirstItemByDefault()
    {
        var settings = new UserSettings();
        settings.SetPluginSetting("plugin", "items", new List<string> { "a", "b" });

        var field = ScalarArrayField(settings, new List<string>());

        Assert.AreSame(field.ArrayItems[0], field.SelectedArrayItem);
    }

    [TestMethod]
    public void AddArrayItem_ScalarArray_AppendsEmptyStringItemAndSelectsIt()
    {
        var field = ScalarArrayField(new UserSettings(), new List<string>());
        var initialCount = field.ArrayItems.Count;

        field.AddCommand.Execute(null);

        Assert.HasCount(initialCount + 1, field.ArrayItems);
        Assert.AreSame(field.ArrayItems[^1], field.SelectedArrayItem);
        Assert.AreEqual("", field.ArrayItems[^1].GetValue());
    }

    [TestMethod]
    public void AddArrayItem_ObjectArray_AppendsItemWithSubFieldDefaults()
    {
        var objectField = new PluginConfigField
        {
            Key = "items",
            FieldType = ConfigFieldType.Array,
            DefaultValue = new List<object>(),
            SubFields = new List<PluginConfigField> { new() { Key = "name", FieldType = ConfigFieldType.Text, DefaultValue = "default-name" } },
        };
        var field = new PluginConfigFieldViewModel("plugin", objectField, new UserSettings(), null);

        field.AddCommand.Execute(null);

        var value = field.ArrayItems[0].GetValue() as Dictionary<string, object?>;
        Assert.AreEqual("default-name", value!["name"]);
    }

    [TestMethod]
    public void DeleteArrayItem_RemovesItemAndPersistsRemainingItemsOnCommit()
    {
        // Deleting an item updates the field's in-memory LocalValueStore/ArrayItems immediately (via
        // SaveArrayFromChildren), but only Commit() actually writes it through to UserSettings -- the
        // same two-phase "edit in-memory, persist on Commit" flow the Settings page's own Save uses.
        var settings = new UserSettings();
        var field = ScalarArrayField(settings, new List<string> { "a", "b" });
        var itemToDelete = field.ArrayItems[0];

        itemToDelete.DeleteCommand.Execute(null);
        field.Commit();

        // Commit() stores the raw List<object?> built from ArrayItems -- no JSON round-trip happens
        // without a real file Save()/Load(), so requesting it back typed as List<string>? would fail
        // GetPluginSetting's type coercion and silently return the default instead.
        var saved = settings.GetPluginSetting<List<object?>?>("plugin", "items", null);
        CollectionAssert.AreEqual(new object?[] { "b" }, saved);
    }

    [TestMethod]
    public void DeleteArrayItem_WasSelected_SelectsNextRemainingItem()
    {
        var field = ScalarArrayField(new UserSettings(), new List<string> { "a", "b", "c" });
        field.SelectedArrayItem = field.ArrayItems[0];

        field.ArrayItems[0].DeleteCommand.Execute(null);

        Assert.AreEqual("b", field.SelectedArrayItem?.GetValue());
    }

    [TestMethod]
    public void DeleteArrayItem_LastRemainingItem_ClearsSelection()
    {
        var field = ScalarArrayField(new UserSettings(), new List<string> { "only" });
        field.SelectedArrayItem = field.ArrayItems[0];

        field.ArrayItems[0].DeleteCommand.Execute(null);

        Assert.IsNull(field.SelectedArrayItem);
    }

    [TestMethod]
    public void MoveUpCommand_MiddleItem_SwapsWithPrevious()
    {
        var field = ScalarArrayField(new UserSettings(), new List<string> { "a", "b", "c" });
        var b = field.ArrayItems[1];

        b.MoveUpCommand.Execute(null);

        CollectionAssert.AreEqual(new[] { "b", "a", "c" }, field.ArrayItems.Select(i => i.GetValue()).ToList());
    }

    [TestMethod]
    public void MoveDownCommand_MiddleItem_SwapsWithNext()
    {
        var field = ScalarArrayField(new UserSettings(), new List<string> { "a", "b", "c" });
        var b = field.ArrayItems[1];

        b.MoveDownCommand.Execute(null);

        CollectionAssert.AreEqual(new[] { "a", "c", "b" }, field.ArrayItems.Select(i => i.GetValue()).ToList());
    }

    [TestMethod]
    public void MoveUpCommand_FirstItem_DoesNothing()
    {
        var field = ScalarArrayField(new UserSettings(), new List<string> { "a", "b" });

        field.ArrayItems[0].MoveUpCommand.Execute(null);

        CollectionAssert.AreEqual(new[] { "a", "b" }, field.ArrayItems.Select(i => i.GetValue()).ToList());
    }

    [TestMethod]
    public void MoveDownCommand_LastItem_DoesNothing()
    {
        var field = ScalarArrayField(new UserSettings(), new List<string> { "a", "b" });

        field.ArrayItems[^1].MoveDownCommand.Execute(null);

        CollectionAssert.AreEqual(new[] { "a", "b" }, field.ArrayItems.Select(i => i.GetValue()).ToList());
    }

    [TestMethod]
    public void MoveUpCommand_ReorderIsPersistedOnCommit()
    {
        var settings = new UserSettings();
        var field = ScalarArrayField(settings, new List<string> { "a", "b" });

        field.ArrayItems[1].MoveUpCommand.Execute(null);
        field.Commit();

        var saved = settings.GetPluginSetting<List<object?>?>("plugin", "items", null);
        CollectionAssert.AreEqual(new object?[] { "b", "a" }, saved);
    }
}
