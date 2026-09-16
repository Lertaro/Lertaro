using Lertaro.App.ViewModels.Settings.Plugins;
using Lertaro.Core;
using Lertaro.PluginSdk.Abstractions;

namespace Lertaro.App.Tests.ViewModels.Settings.Plugins;

// The Settings window's Apply gate reads every built page's errors (SettingsViewModel.ValidationErrors),
// and this is the leaf that produces them for a plugin config field. Three things matter here and none is
// visible from the outside: a message must name the field that produced it, asking for the errors must NOT
// materialize a config tree that was never shown (building that tree is the exact cost the lazy rows exist
// to avoid, and Apply sits behind a single click), and only a value the user has STAGED may block a save --
// a schema default or a carried-over value that cannot work is shown next to its field instead, or an
// upgraded install could not save anything at all.
[TestClass]
public sealed class PluginConfigFieldValidationSupportTests
{
    // ':' is read by the search syntax as the exclusion operator, so a trigger claiming it never fires.
    private const string Reserved = ":";

    private static PluginConfigFieldViewModel Field(PluginConfigField schema)
        => new("plugin", schema, new UserSettings(), null);

    private static PluginConfigField Schema(
        string key,
        string value,
        int maxLength = 0,
        ConfigFieldValidation validation = ConfigFieldValidation.None) => new()
        {
            Key = key,
            FieldType = ConfigFieldType.Text,
            MaxLength = maxLength,
            DefaultValue = value,
            Validation = validation,
        };

    // A one-character Text field IS a query-token trigger (see QueryTokenPrefixRules.IsPrefixField), which
    // is the only reason such a field exists.
    private static PluginConfigField TriggerSchema(string key, string value)
        => Schema(key, value, maxLength: 1);

    [TestMethod]
    public void PrefixError_ReservedCharacter_IsReportedAndNamesTheField()
    {
        var field = Field(TriggerSchema("prefix", Reserved));
        field.Value = Reserved; // the user typed it

        var errors = field.Validation.Errors.ToList();

        Assert.HasCount(1, errors);
        StringAssert.StartsWith(errors[0], "plugin.prefix: ");
    }

    [TestMethod]
    public void PrefixError_UnstagedReservedValue_IsShownButDoesNotBlockSaving()
    {
        // The schema's own default (or a value a previous release persisted) is not something the user is
        // entering now. Blocking on it refuses every other setting in the window -- the lockout this rule
        // exists to prevent -- while the field still shows the warning.
        var field = Field(TriggerSchema("prefix", Reserved));

        Assert.IsNotNull(field.PrefixError, "the field must still report that the value cannot work");
        Assert.IsFalse(field.IsDirty);
        Assert.IsEmpty(field.Validation.Errors.ToList());
    }

    [TestMethod]
    public void PrefixError_FieldThatIsNotATrigger_IsNotReported()
    {
        // The same reserved character in an ordinary text field is just the user's text: only the
        // single-character trigger shape is matched against the syntax.
        var field = Field(Schema("label", Reserved));
        field.Value = Reserved;

        Assert.IsEmpty(field.Validation.Errors.ToList());
    }

    [TestMethod]
    public void TriggerKeywordError_ReservedLeadingCharacter_IsReportedAndNamesTheField()
    {
        var field = Field(Schema("keyword", Reserved + "audio", validation: ConfigFieldValidation.TriggerKeyword));
        field.Value = Reserved + "audio";

        var errors = field.Validation.Errors.ToList();

        Assert.HasCount(1, errors);
        StringAssert.StartsWith(errors[0], "plugin.keyword: ");
    }

    [TestMethod]
    public void TriggerKeywordError_UsableKeyword_IsNotReported()
    {
        var field = Field(Schema("keyword", "audio", validation: ConfigFieldValidation.TriggerKeyword));

        Assert.IsEmpty(field.Validation.Errors.ToList());
    }

    [TestMethod]
    public void TriggerKeywordError_FieldWithoutTheTriggerKeywordValidation_IsNotReported()
    {
        // The keyword rule only applies where the schema declares the field as one; a reserved leading
        // character anywhere else is not a trigger and must not be reported.
        var field = Field(Schema("keyword", Reserved + "audio"));

        Assert.IsEmpty(field.Validation.Errors.ToList());
    }

    [TestMethod]
    public void Errors_UnshownContainer_IsNotMaterializedToLookForThem()
    {
        // A group's rows are built on first access. An unshown group holds no staged edit, so it cannot
        // hold an error -- and building it here would make Apply walk every visited plugin's whole schema.
        var field = Field(GroupSchema());

        Assert.IsEmpty(field.Validation.Errors.ToList());
        Assert.IsFalse(field.HasLoadedChildren, "asking for the errors must not build the group's rows");
    }

    [TestMethod]
    public void Errors_ShownContainer_ReportsItsRowsErrors()
    {
        var field = Field(GroupSchema());
        _ = field.Children; // the group was shown
        field.Children[0].Value = Reserved; // and its row was edited

        var errors = field.Validation.Errors.ToList();

        Assert.HasCount(1, errors);
        StringAssert.StartsWith(errors[0], "plugin.prefix: ");
    }

    [TestMethod]
    public void Errors_UnshownArray_IsNotMaterializedToLookForThem()
    {
        var field = Field(ArraySchema());

        Assert.IsEmpty(field.Validation.Errors.ToList());
        Assert.IsFalse(field.HasLoadedArrayItems, "asking for the errors must not build the array's rows");
    }

    [TestMethod]
    public void Errors_ShownArray_ReportsItsRowsErrors()
    {
        var field = Field(ArraySchema());
        _ = field.ArrayItems; // the array was shown, and starts empty

        Assert.IsEmpty(field.Validation.Errors.ToList());

        var item = new PluginConfigArrayItemViewModel(field, new Dictionary<string, object>(), () => { });
        field.ArrayItems.Add(item);
        item.Children[0].Value = Reserved; // the row's own trigger was edited

        var errors = field.Validation.Errors.ToList();

        Assert.HasCount(1, errors);
        StringAssert.StartsWith(errors[0], "plugin.prefix: ");
    }

    private static PluginConfigField GroupSchema() => new()
    {
        Key = "group",
        FieldType = ConfigFieldType.Group,
        SubFields = [TriggerSchema("prefix", Reserved)],
    };

    private static PluginConfigField ArraySchema() => new()
    {
        Key = "items",
        FieldType = ConfigFieldType.Array,
        SubFields = [TriggerSchema("prefix", Reserved)],
        DefaultValue = new List<object>(),
    };
}
