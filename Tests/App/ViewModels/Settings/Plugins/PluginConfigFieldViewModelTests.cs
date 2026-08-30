using Lertaro.Core;
using Lertaro.PluginSdk.Abstractions;
using Lertaro.App.ViewModels.Settings.Plugins;

namespace Lertaro.App.Tests.ViewModels.Settings.Plugins;

[TestClass]
public sealed class PluginConfigFieldViewModelTests
{
    private static PluginConfigField Field(ConfigFieldType type, object defaultValue, string key = "myKey") => new()
    {
        Key = key,
        FieldType = type,
        DefaultValue = defaultValue,
    };

    [TestMethod]
    public void Label_LabelKeySet_ReturnsLiteralOrTranslatedText()
    {
        var vm = new PluginConfigFieldViewModel("plugin", new PluginConfigField { LabelKey = "MyLabel", FieldType = ConfigFieldType.Text, DefaultValue = "" }, new UserSettings(), () => { });

        Assert.AreEqual("MyLabel", vm.Label);
    }

    [TestMethod]
    public void Label_LabelKeyEmpty_ReturnsEmptyString()
    {
        var vm = new PluginConfigFieldViewModel("plugin", new PluginConfigField { FieldType = ConfigFieldType.Text, DefaultValue = "" }, new UserSettings(), () => { });

        Assert.AreEqual("", vm.Label);
    }

    [TestMethod]
    public void FieldTypeFlags_ReflectSchemaFieldType()
    {
        var vm = new PluginConfigFieldViewModel("plugin", Field(ConfigFieldType.Boolean, false), new UserSettings(), () => { });

        Assert.IsTrue(vm.IsBoolean);
        Assert.IsFalse(vm.IsText);
        Assert.IsFalse(vm.IsInteger);
    }

    [TestMethod]
    public void IsObjectArray_ArrayWithSubFields_ReturnsTrueAndScalarArrayFalse()
    {
        var field = Field(ConfigFieldType.Array, new List<object>());
        field.SubFields = new List<PluginConfigField> { Field(ConfigFieldType.Text, "") };
        var vm = new PluginConfigFieldViewModel("plugin", field, new UserSettings(), () => { });

        Assert.IsTrue(vm.IsObjectArray);
        Assert.IsFalse(vm.IsScalarArray);
    }

    [TestMethod]
    public void IsScalarArray_ArrayWithNoSubFields_ReturnsTrue()
    {
        var vm = new PluginConfigFieldViewModel("plugin", Field(ConfigFieldType.Array, new List<object>()), new UserSettings(), () => { });

        Assert.IsTrue(vm.IsScalarArray);
        Assert.IsFalse(vm.IsObjectArray);
    }

    [TestMethod]
    public void IsIconField_KeyIsIconCaseInsensitive_ReturnsTrue()
    {
        var vm = new PluginConfigFieldViewModel("plugin", Field(ConfigFieldType.Text, "", key: "ICON"), new UserSettings(), () => { });

        Assert.IsTrue(vm.IsIconField);
    }

    [TestMethod]
    public void LocalValueStore_WithOnValueChanged_UsesSchemaDefaultNotSettings()
    {
        // A field with a change callback (a child of an array item) never touches UserSettings for its
        // initial value -- it starts from the schema default, since its actual value is owned by the
        // parent array item, not the plugin's own persisted settings.
        var settings = new UserSettings();
        settings.SetPluginSetting("plugin", "myKey", "from-settings");
        var vm = new PluginConfigFieldViewModel("plugin", Field(ConfigFieldType.Text, "schema-default"), settings, () => { });

        Assert.AreEqual("schema-default", vm.LocalValueStore);
    }

    [TestMethod]
    public void LocalValueStore_NoOnValueChanged_ReadsFromSettings()
    {
        var settings = new UserSettings();
        settings.SetPluginSetting("plugin", "myKey", "from-settings");
        var vm = new PluginConfigFieldViewModel("plugin", Field(ConfigFieldType.Text, "schema-default"), settings, null);

        Assert.AreEqual("from-settings", vm.LocalValueStore);
    }

    [TestMethod]
    public void LocalValueStore_NoOnValueChangedAndNoSettingPersisted_FallsBackToSchemaDefault()
    {
        var vm = new PluginConfigFieldViewModel("plugin", Field(ConfigFieldType.Text, "schema-default"), new UserSettings(), null);

        Assert.AreEqual("schema-default", vm.LocalValueStore);
    }

    [TestMethod]
    public void Value_StringListGet_JoinsListWithCrLf()
    {
        var vm = new PluginConfigFieldViewModel("plugin", Field(ConfigFieldType.StringList, new List<string>()), new UserSettings(), () => { })
        {
            LocalValueStore = new List<string> { "a", "b" }
        };

        Assert.AreEqual("a\r\nb", vm.Value);
    }

    [TestMethod]
    public void Value_StringListSet_SplitsTextIntoTrimmedList()
    {
        var vm = new PluginConfigFieldViewModel("plugin", Field(ConfigFieldType.StringList, new List<string>()), new UserSettings(), () => { })
        {
            Value = "a\r\n b \nc"
        };

        CollectionAssert.AreEqual(new[] { "a", "b", "c" }, (List<string>)vm.LocalValueStore!);
    }

    [TestMethod]
    public void Value_IntegerSet_ConvertsStringToInt()
    {
        var vm = new PluginConfigFieldViewModel("plugin", Field(ConfigFieldType.Integer, 0), new UserSettings(), () => { })
        {
            Value = "42"
        };

        Assert.AreEqual(42, vm.LocalValueStore);
    }

    [TestMethod]
    public void Value_ObjectFieldType_ReturnsSelf()
    {
        var vm = new PluginConfigFieldViewModel("plugin", Field(ConfigFieldType.Object, null!), new UserSettings(), () => { });

        Assert.AreSame(vm, vm.Value);
    }

    [TestMethod]
    public void Commit_GroupField_RecursesIntoChildrenAndPersistsThem()
    {
        var settings = new UserSettings();
        var group = new PluginConfigField
        {
            Key = "group",
            FieldType = ConfigFieldType.Group,
            SubFields = new List<PluginConfigField> { Field(ConfigFieldType.Text, "default", key: "child") },
        };
        var vm = new PluginConfigFieldViewModel("plugin", group, settings, null);
        vm.Children[0].Value = "committed-value";

        vm.Commit();

        Assert.AreEqual("committed-value", settings.GetPluginSetting<string?>("plugin", "child", null));
    }

    [TestMethod]
    public void Commit_RequireNonEmptyFieldLeftBlank_FallsBackToSchemaDefault()
    {
        var settings = new UserSettings();
        var field = Field(ConfigFieldType.Text, "the-default");
        field.RequireNonEmpty = true;
        var vm = new PluginConfigFieldViewModel("plugin", field, settings, null) { Value = "   " };

        vm.Commit();

        Assert.AreEqual("the-default", settings.GetPluginSetting<string?>("plugin", "myKey", null));
    }

    [TestMethod]
    public void Commit_NonEmptyValue_PersistsAsIs()
    {
        var settings = new UserSettings();
        var vm = new PluginConfigFieldViewModel("plugin", Field(ConfigFieldType.Text, "default"), settings, null) { Value = "user-value" };

        vm.Commit();

        Assert.AreEqual("user-value", settings.GetPluginSetting<string?>("plugin", "myKey", null));
    }

    [TestMethod]
    public void ButtonField_Click_InvokesOnClickAndCommitStoresNothing()
    {
        var clicks = 0;
        var field = Field(ConfigFieldType.Button, string.Empty);
        field.OnClick = () => clicks++;
        var settings = new UserSettings();
        var vm = new PluginConfigFieldViewModel("plugin", field, settings, null);

        Assert.IsTrue(vm.IsButton);
        vm.ButtonClickCommand.Execute(null);
        Assert.AreEqual(1, clicks);

        // A button has no value of its own: committing must not write a settings key.
        vm.Commit();
        Assert.AreEqual("sentinel", settings.GetPluginSetting<string?>("plugin", "myKey", "sentinel"));
    }
}
