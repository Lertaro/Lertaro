using Lertaro.App.Helpers;
using Lertaro.App.ViewModels.Settings.Plugins;
using Lertaro.Core;
using Lertaro.PluginSdk.Abstractions;

namespace Lertaro.App.Tests.ViewModels.Settings.Plugins;

// How one config field's help text is produced, which is also the only place the host can say something a
// plugin cannot say for itself. Two of those:
//
//   - a PREFIX field may carry "{0}", which the host fills with the characters that prefix cannot be. That
//     set belongs to the search syntax (SearchSyntaxReserved) and a plugin assembly cannot reference it, so
//     the plugin's own copy of the list is what shipped wrong twice: it named the '\' the field itself
//     defaults to, and was already missing the characters the syntax had gained since.
//   - a TOKEN KEYWORD field gets a sentence naming the whole token to type ("\audio"), which is the
//     app-wide prefix plus what the user has typed -- neither of which the plugin knows.
//
// Translation keys do not resolve in this assembly (the lookups fall back to "[Key]"), which is exactly why
// the wording rules live in a pure static method here: through the property the hint key never resolves, so
// only the prefix branch could be asserted.
[TestClass]
public sealed class PluginConfigFieldDisplaySupportTests
{
    private const string Placeholder = "cannot be ({0})";

    private static PluginConfigFieldViewModel Field(PluginConfigField schema)
        => new("plugin", schema, new UserSettings(), null);

    // A one-character Text field IS a query-token trigger, which is the only reason such a field exists
    // (see QueryTokenPrefixRules.IsPrefixField).
    private static PluginConfigField TriggerSchema() => new()
    {
        Key = "prefix",
        FieldType = ConfigFieldType.Text,
        MaxLength = 1,
        DefaultValue = "\\",
    };

    [TestMethod]
    public void Description_PrefixFieldWithThePlaceholder_NamesTheCharactersTheSyntaxOwns()
    {
        var schema = TriggerSchema();
        schema.DescriptionKey = Placeholder;

        var description = Field(schema).Description;

        Assert.AreEqual(
            $"cannot be ({SearchSyntaxReserved.DescribeUnusableTokenPrefixCharacters()})",
            description);
    }

    [TestMethod]
    public void Description_PrefixField_NeverNamesTheCharacterItDefaultsTo()
    {
        // The contradiction this replaced: the shipped text said the prefix may not be one of
        // "\\ < > : *" while the field's own default was "\\", so the help text forbade its default.
        var schema = TriggerSchema();
        schema.DescriptionKey = Placeholder;

        var description = Field(schema).Description;

        Assert.DoesNotContain(SearchSyntaxReserved.TokenPrefixCharacter.ToString(), description);
    }

    [TestMethod]
    public void Description_FieldThatIsNotATrigger_LeavesThePlaceholderAlone()
    {
        // A plugin may put "{0}" in a description for its own reasons; only the single-character trigger
        // shape gets the host's list.
        var schema = new PluginConfigField
        {
            Key = "label",
            FieldType = ConfigFieldType.Text,
            DescriptionKey = Placeholder,
        };

        Assert.AreEqual(Placeholder, Field(schema).Description);
    }

    [TestMethod]
    public void Description_PrefixFieldWithoutThePlaceholder_IsReturnedUnchanged()
    {
        var schema = TriggerSchema();
        schema.DescriptionKey = "must be one character";

        Assert.AreEqual("must be one character", Field(schema).Description);
    }

    [TestMethod]
    public void Description_MalformedPlaceholder_DoesNotThrowOutOfTheProperty()
    {
        // A translated format string is not a programmer's string. A locale that mistypes the placeholder
        // would otherwise throw out of a bound property, leaving the row with no help text at all and a
        // binding error in the log -- strictly worse than showing the text unfilled. This project has
        // already shipped broken translation resources twice.
        var schema = TriggerSchema();
        schema.DescriptionKey = "cannot be ({0}), (o) is fine, {o} is not";

        var description = Field(schema).Description;

        Assert.AreEqual("cannot be ({0}), (o) is fine, {o} is not", description);
    }

    // --- the token keyword hint ---------------------------------------------------------------------
    //
    // The sentence the user actually needs, and the one no plugin can write: which WORD to type, which is
    // the app-wide prefix plus whatever they have typed into the field. Tested as the pure function it is,
    // because this assembly has no translations loaded -- through the property the hint key never resolves,
    // so the wording rules would be unpinnable.

    private const string Hint = "Type '{0}' in the search box.";

    [TestMethod]
    public void TokenHint_KeywordTyped_NamesTheWholeTokenWithTheHostPrefix()
        => Assert.AreEqual(
            "Trigger keyword for this filter. Type '\\audio' in the search box.",
            PluginConfigFieldDisplaySupport.TokenHint("Trigger keyword for this filter.", Hint, '\\', "audio"));

    [TestMethod]
    public void TokenHint_NoKeywordYet_SaysNothing()
    {
        // A hint built from an empty keyword reads "Type '\' in the search box", which is worse than the
        // base text on its own -- so the sentence is withheld until there is something to put in it.
        Assert.IsNull(PluginConfigFieldDisplaySupport.TokenHint("Trigger keyword.", Hint, '\\', ""));
        Assert.IsNull(PluginConfigFieldDisplaySupport.TokenHint("Trigger keyword.", Hint, '\\', "   "));
        Assert.IsNull(PluginConfigFieldDisplaySupport.TokenHint("Trigger keyword.", Hint, '\\', null));
    }

    // The prefix is not a constant of this sentence: it is whatever Settings → General → System holds.
    [TestMethod]
    public void TokenHint_UserConfiguredPrefix_IsTheOneShown()
        => Assert.AreEqual(
            "base Type '?doc' in the search box.",
            PluginConfigFieldDisplaySupport.TokenHint("base", Hint, '?', "doc"));

    [TestMethod]
    public void TokenHint_SurroundingWhitespaceInTheKeyword_IsTrimmed()
        => Assert.AreEqual(
            "base Type '\\doc' in the search box.",
            PluginConfigFieldDisplaySupport.TokenHint("base", Hint, '\\', " doc "));

    [TestMethod]
    public void TokenHint_NoBaseTextAndNoTemplate_BehaveAsDocumented()
    {
        // No base text: the hint stands alone rather than starting with a space.
        Assert.AreEqual("Type '\\doc' in the search box.",
            PluginConfigFieldDisplaySupport.TokenHint(null, Hint, '\\', "doc"));
        Assert.AreEqual("Type '\\doc' in the search box.",
            PluginConfigFieldDisplaySupport.TokenHint("", Hint, '\\', "doc"));

        // No translatable template (a locale that has not been updated): no sentence at all, not a crash.
        Assert.IsNull(PluginConfigFieldDisplaySupport.TokenHint("base", null, '\\', "doc"));
        Assert.IsNull(PluginConfigFieldDisplaySupport.TokenHint("base", "", '\\', "doc"));
    }
}
