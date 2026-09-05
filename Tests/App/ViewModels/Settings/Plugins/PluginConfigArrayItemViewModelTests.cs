using Lertaro.Core;
using Lertaro.PluginSdk.Abstractions;
using Lertaro.App.ViewModels.Settings.Plugins;

namespace Lertaro.App.Tests.ViewModels.Settings.Plugins;

[TestClass]
public sealed class PluginConfigArrayItemViewModelTests
{
    private static PluginConfigFieldViewModel ArrayField(List<PluginConfigField>? subFields) => new(
        "plugin",
        new PluginConfigField { Key = "items", FieldType = ConfigFieldType.Array, SubFields = subFields, DefaultValue = new List<object>() },
        new UserSettings(),
        null);

    [TestMethod]
    public void Constructor_ScalarItem_PopulatesSimpleValueViewModel()
    {
        var parent = ArrayField(null);

        var item = new PluginConfigArrayItemViewModel(parent, "hello", () => { });

        Assert.IsNotNull(item.SimpleValueViewModel);
        Assert.AreEqual("hello", item.SimpleValueViewModel!.LocalValueStore);
        Assert.IsEmpty(item.Children);
    }

    [TestMethod]
    public void Constructor_ObjectItem_PopulatesChildrenFromSubFieldsAndInitialValue()
    {
        var parent = ArrayField(new List<PluginConfigField>
        {
            new() { Key = "name", FieldType = ConfigFieldType.Text, DefaultValue = "" },
            new() { Key = "enabled", FieldType = ConfigFieldType.Boolean, DefaultValue = false },
        });
        var initialValue = new Dictionary<string, object> { ["name"] = "Widget", ["enabled"] = true };

        var item = new PluginConfigArrayItemViewModel(parent, initialValue, () => { });

        Assert.IsNull(item.SimpleValueViewModel);
        Assert.HasCount(2, item.Children);
        Assert.AreEqual("Widget", item.Children[0].LocalValueStore);
        Assert.IsTrue((bool)item.Children[1].LocalValueStore!);
    }

    [TestMethod]
    public void Constructor_ObjectItemMissingSubFieldValue_FallsBackToSubFieldDefault()
    {
        var parent = ArrayField(new List<PluginConfigField>
        {
            new() { Key = "name", FieldType = ConfigFieldType.Text, DefaultValue = "default-name" },
        });

        var item = new PluginConfigArrayItemViewModel(parent, new Dictionary<string, object>(), () => { });

        Assert.AreEqual("default-name", item.Children[0].LocalValueStore);
    }

    [TestMethod]
    public void TitleField_FirstTextSubField_IsUsedAsTitle()
    {
        var parent = ArrayField(new List<PluginConfigField>
        {
            new() { Key = "flag", FieldType = ConfigFieldType.Boolean, DefaultValue = false },
            new() { Key = "name", FieldType = ConfigFieldType.Text, DefaultValue = "" },
        });

        var item = new PluginConfigArrayItemViewModel(parent, new Dictionary<string, object>(), () => { });

        Assert.AreEqual("name", item.TitleField?.SchemaField.Key);
    }

    [TestMethod]
    public void BadgeField_SecondTextSubField_IsUsedAsBadge()
    {
        var parent = ArrayField(new List<PluginConfigField>
        {
            new() { Key = "name", FieldType = ConfigFieldType.Text, DefaultValue = "" },
            new() { Key = "note", FieldType = ConfigFieldType.Text, DefaultValue = "" },
        });

        var item = new PluginConfigArrayItemViewModel(parent, new Dictionary<string, object>(), () => { });

        Assert.AreEqual("note", item.BadgeField?.SchemaField.Key);
    }

    [TestMethod]
    public void BadgeField_IconSubField_IsNotUsedAsBadge()
    {
        var parent = ArrayField(new List<PluginConfigField>
        {
            new() { Key = "name", FieldType = ConfigFieldType.Text, DefaultValue = "" },
            new() { Key = "Icon", FieldType = ConfigFieldType.Text, DefaultValue = "" },
            new() { Key = "rule", FieldType = ConfigFieldType.Text, DefaultValue = "" },
        });

        var item = new PluginConfigArrayItemViewModel(parent, new Dictionary<string, object>(), () => { });

        Assert.AreEqual("rule", item.BadgeField?.SchemaField.Key);
    }

    [TestMethod]
    public void GetValue_ScalarItem_ReturnsSimpleValue()
    {
        var parent = ArrayField(null);
        var item = new PluginConfigArrayItemViewModel(parent, "hello", () => { });

        Assert.AreEqual("hello", item.GetValue());
    }

    [TestMethod]
    public void GetValue_ObjectItem_ReturnsDictionaryOfChildValues()
    {
        var parent = ArrayField(new List<PluginConfigField>
        {
            new() { Key = "name", FieldType = ConfigFieldType.Text, DefaultValue = "" },
        });
        var item = new PluginConfigArrayItemViewModel(parent, new Dictionary<string, object> { ["name"] = "Widget" }, () => { });

        var value = item.GetValue() as Dictionary<string, object?>;

        Assert.IsNotNull(value);
        Assert.AreEqual("Widget", value["name"]);
    }

    [TestMethod]
    public void DeleteCommand_Execute_InvokesOnDelete()
    {
        var parent = ArrayField(null);
        var called = false;
        var item = new PluginConfigArrayItemViewModel(parent, "x", () => called = true);

        item.DeleteCommand.Execute(null);

        Assert.IsTrue(called);
    }

    [TestMethod]
    public void MoveUpCommand_Execute_InvokesOnMoveUp()
    {
        var parent = ArrayField(null);
        var called = false;
        var item = new PluginConfigArrayItemViewModel(parent, "x", () => { }, onMoveUp: () => called = true);

        item.MoveUpCommand.Execute(null);

        Assert.IsTrue(called);
    }

    [TestMethod]
    public void MoveDownCommand_Execute_InvokesOnMoveDown()
    {
        var parent = ArrayField(null);
        var called = false;
        var item = new PluginConfigArrayItemViewModel(parent, "x", () => { }, onMoveDown: () => called = true);

        item.MoveDownCommand.Execute(null);

        Assert.IsTrue(called);
    }

    [TestMethod]
    public void MoveUpCommand_NoCallbackProvided_ExecutesWithoutThrowing()
    {
        var parent = ArrayField(null);
        var item = new PluginConfigArrayItemViewModel(parent, "x", () => { });

        item.MoveUpCommand.Execute(null);
        item.MoveDownCommand.Execute(null);
    }

    [TestMethod]
    public void Constructor_ScalarItemWithBooleanDefault_InfersBooleanFieldType()
    {
        var field = new PluginConfigField { Key = "flags", FieldType = ConfigFieldType.Array, DefaultValue = false };
        var parentSettings = new UserSettings();
        var parent = new PluginConfigFieldViewModel("plugin", field, parentSettings, null);

        var item = new PluginConfigArrayItemViewModel(parent, true, () => { });

        Assert.AreEqual(ConfigFieldType.Boolean, item.SimpleValueViewModel!.FieldType);
    }
}
