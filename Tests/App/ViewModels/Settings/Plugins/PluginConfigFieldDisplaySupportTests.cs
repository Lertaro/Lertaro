using Lertaro.App.ViewModels.Settings.Plugins;
using Lertaro.Core;
using Lertaro.PluginSdk.Abstractions;

namespace Lertaro.App.Tests.ViewModels.Settings.Plugins;

// How one config field's help text is produced, which is also the only place the host can say something a
// plugin cannot say for itself: a TOKEN KEYWORD field gets a sentence naming the whole token to type
// ("\audio"), which is the app-wide prefix plus what the user has typed -- neither of which the plugin
// knows.
//
// Translation keys do not resolve in this assembly (the lookups fall back to "[Key]"), which is exactly why
// the wording rules live in a pure static method here: through the property the hint key never resolves, so
// going through Description could only ever assert that the base text came back.
[TestClass]
public sealed class PluginConfigFieldDisplaySupportTests
{
    private static PluginConfigFieldViewModel Field(PluginConfigField schema)
        => new("plugin", schema, new UserSettings(), null);

    // The host used to fill a "{0}" in a PREFIX field's description with the characters that prefix could
    // not be, because a plugin assembly cannot reference the host's own list. There are no per-plugin prefix
    // fields any more (a plugin reads the app-wide prefix through the SDK), so nothing is formatted into a
    // plugin's text at all -- and that is what this pins, since a "{0}" reappearing unfilled on screen is
    // silent.
    [TestMethod]
    public void Description_PlaceholderInAField_IsLeftAlone()
    {
        var schema = new PluginConfigField
        {
            Key = "label",
            FieldType = ConfigFieldType.Text,
            DescriptionKey = "cannot be ({0})",
        };

        Assert.AreEqual("cannot be ({0})", Field(schema).Description);
    }

    // --- the token keyword hint ---------------------------------------------------------------------

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

    // A translated format string is not a programmer's string. A locale that mistypes the placeholder
    // would otherwise throw out of a bound property, leaving the row with no help text at all plus a
    // binding error in the log. This project has already shipped broken translation resources twice.
    [TestMethod]
    public void TokenHint_MalformedPlaceholder_DoesNotThrowOutOfTheProperty()
        => Assert.AreEqual("base Type '{O}' here.",
            PluginConfigFieldDisplaySupport.TokenHint("base", "Type '{O}' here.", '\\', "doc"));
}
