using Lertaro.App.Helpers;
using Lertaro.Core;
using Lertaro.PluginSdk.Abstractions;

namespace Lertaro.App.Tests.Helpers;

// Instant-answer trigger keywords saved with a leading character the query-token scanner lifts the whole word
// away for -- the configured token prefix, '<' or '>'. Such a keyword blanks the file results every time it is
// used (the lifted word is a token no provider claims), which is what makes it worth resetting to the keyword
// the plugin's own schema ships. The stored value is cleared and reported once -- the removal is what makes it
// unrepeatable.
//
// Note what is NOT in that list: the precision-inversion character. Instant providers are handed the untouched
// box text, so a "?word" keyword fires, and PluginTriggerQuery.Strip takes it out of the file-search text like
// any other trigger word. This migration used to clear those anyway.
//
// Only the RULE is covered here; which plugin settings are trigger keywords is discovered by walking loaded
// plugin assemblies' schemas, which is assembly-bound with no injectable seam. The candidate fields are handed
// in for exactly that reason.
[TestClass]
public sealed class PluginTriggerKeywordMigrationTests
{
    private const string PrecisionTrigger = "?";
    private const string LiftedTrigger = "\\";

    [TestMethod]
    public void TakeUnusable_ScannerLiftedKeyword_IsClearedAndReported()
    {
        var settings = WithKeyword("Lertaro.Plugins.AudioDeviceSelector", "TriggerKeyword", LiftedTrigger + "ad");

        var cleared = PluginTriggerKeywordMigration.TakeUnusable(settings, Candidates(("Lertaro.Plugins.AudioDeviceSelector", "AudioDeviceSelector", "TriggerKeyword", ConfigFieldValidation.TriggerKeyword)));

        Assert.HasCount(1, cleared);
        Assert.AreEqual("Lertaro.Plugins.AudioDeviceSelector", cleared[0].PluginId);
        Assert.AreEqual("AudioDeviceSelector", cleared[0].PluginName);
        Assert.AreEqual("TriggerKeyword", cleared[0].Key);
        Assert.AreEqual(LiftedTrigger + "ad", cleared[0].Value);
        Assert.IsNull(settings.GetPluginSetting<string?>("Lertaro.Plugins.AudioDeviceSelector", "TriggerKeyword", null),
            "the unusable value must be gone, so the provider falls back to the keyword its schema ships");
    }

    [TestMethod]
    public void TakeUnusable_SortFilterTriggerKeyword_IsCleared()
    {
        // '<' and '>' are lifted whatever the configured prefix is, so they are not exempt the way a
        // user-chosen prefix character would be.
        var settings = WithKeyword("plugin", "TriggerKeyword", "<calc");

        var cleared = PluginTriggerKeywordMigration.TakeUnusable(settings, Candidates(("plugin", "Plugin", "TriggerKeyword", ConfigFieldValidation.TriggerKeyword)));

        Assert.HasCount(1, cleared);
        Assert.IsNull(settings.GetPluginSetting<string?>("plugin", "TriggerKeyword", null));
    }

    [TestMethod]
    public void TakeUnusable_PrecisionInversionKeyword_IsKept()
    {
        // The regression this replaces: '?' was treated as dead on the theory that the parser eats it before
        // any provider is asked. It is not one of the characters the scanner lifts and providers get the raw
        // box text, so the keyword worked -- and clearing it destroyed a working setting while the balloon gave
        // the user a reason that did not hold.
        var settings = WithKeyword("plugin", "TriggerKeyword", PrecisionTrigger + "note");

        var cleared = PluginTriggerKeywordMigration.TakeUnusable(settings, Candidates(("plugin", "Plugin", "TriggerKeyword", ConfigFieldValidation.TriggerKeyword)));

        Assert.IsEmpty(cleared);
        Assert.AreEqual(PrecisionTrigger + "note", settings.GetPluginSetting<string?>("plugin", "TriggerKeyword", null));
    }

    [TestMethod]
    public void TakeUnusable_UsableKeyword_IsLeftAlone()
    {
        var settings = WithKeyword("plugin", "TriggerKeyword", "ad");

        var cleared = PluginTriggerKeywordMigration.TakeUnusable(settings, Candidates(("plugin", "Plugin", "TriggerKeyword", ConfigFieldValidation.TriggerKeyword)));

        Assert.IsEmpty(cleared);
        Assert.AreEqual("ad", settings.GetPluginSetting<string?>("plugin", "TriggerKeyword", null));
    }

    [TestMethod]
    public void TakeUnusable_FieldThatIsNotATriggerKeyword_IsLeftAlone()
    {
        // A lifted character anywhere else is the user's own text: only a field that declares itself a trigger
        // keyword is matched against the start of the query, so only one of those can be spoiled by the token
        // scanner lifting it.
        var settings = WithKeyword("plugin", "SomeLabel", LiftedTrigger + "readme");

        var cleared = PluginTriggerKeywordMigration.TakeUnusable(settings, Candidates(("plugin", "Plugin", "SomeLabel", ConfigFieldValidation.None)));

        Assert.IsEmpty(cleared);
        Assert.AreEqual(LiftedTrigger + "readme", settings.GetPluginSetting<string?>("plugin", "SomeLabel", null));
    }

    [TestMethod]
    public void TakeUnusable_NothingStored_FallsBackToTheSchemaDefault()
    {
        var settings = new UserSettings();

        var cleared = PluginTriggerKeywordMigration.TakeUnusable(settings, Candidates(("plugin", "Plugin", "TriggerKeyword", ConfigFieldValidation.TriggerKeyword)));

        Assert.IsEmpty(cleared);
    }

    [TestMethod]
    public void TakeUnusable_IsIdempotent()
    {
        // What makes the notice it feeds a one-time thing: after the clear there is nothing left to detect.
        var settings = WithKeyword("plugin", "TriggerKeyword", LiftedTrigger + "calc");
        var candidates = Candidates(("plugin", "Plugin", "TriggerKeyword", ConfigFieldValidation.TriggerKeyword));

        Assert.HasCount(1, PluginTriggerKeywordMigration.TakeUnusable(settings, candidates));
        Assert.IsEmpty(PluginTriggerKeywordMigration.TakeUnusable(settings, candidates));
    }

    private static UserSettings WithKeyword(string pluginId, string key, string value)
    {
        var settings = new UserSettings();
        settings.SetPluginSetting(pluginId, key, value);
        return settings;
    }

    private static IEnumerable<(string PluginId, string PluginName, PluginConfigField Field)> Candidates(params (string PluginId, string PluginName, string Key, ConfigFieldValidation Validation)[] fields)
        => fields.Select(f => (f.PluginId, f.PluginName, new PluginConfigField
        {
            Key = f.Key,
            FieldType = ConfigFieldType.Text,
            Validation = f.Validation,
        }));
}
