using Lertaro.App.Helpers;
using Lertaro.Core;
using Lertaro.PluginSdk.Abstractions;

namespace Lertaro.App.Tests.Helpers;

// Instant-answer trigger keywords that were saved before '?' became the precision-inversion trigger.
//
// Such a keyword is dead rather than misread: the parser strips the '?' before any provider is asked, so the
// trigger can never fire. The stored value is cleared (the plugin's own schema default then applies, which
// works) and reported once -- the removal is what makes it unrepeatable.
//
// Only the RULE is covered here; which plugin settings are trigger keywords is discovered by walking loaded
// plugin assemblies' schemas, which is assembly-bound with no injectable seam. The candidate fields are handed
// in for exactly that reason.
[TestClass]
public sealed class PluginTriggerKeywordMigrationTests
{
    private const string PrecisionTrigger = "?";

    [TestMethod]
    public void TakeUnusable_PrecisionPrefixedKeyword_IsClearedAndReported()
    {
        var settings = WithKeyword("Lertaro.Plugins.AudioDeviceSelector", "TriggerKeyword", PrecisionTrigger + "ad");

        var cleared = PluginTriggerKeywordMigration.TakeUnusable(settings, Candidates(("Lertaro.Plugins.AudioDeviceSelector", "AudioDeviceSelector", "TriggerKeyword", ConfigFieldValidation.TriggerKeyword)));

        Assert.HasCount(1, cleared);
        Assert.AreEqual("Lertaro.Plugins.AudioDeviceSelector", cleared[0].PluginId);
        Assert.AreEqual("AudioDeviceSelector", cleared[0].PluginName);
        Assert.AreEqual("TriggerKeyword", cleared[0].Key);
        Assert.AreEqual(PrecisionTrigger + "ad", cleared[0].Value);
        Assert.IsNull(settings.GetPluginSetting<string?>("Lertaro.Plugins.AudioDeviceSelector", "TriggerKeyword", null),
            "the dead value must be gone, so the provider falls back to the keyword its schema ships");
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
        // A '?' anywhere else is the user's own text: only a field that declares itself a trigger keyword is
        // matched against the start of the query, so only one of those can have been retired by the operator.
        var settings = WithKeyword("plugin", "SomeLabel", PrecisionTrigger + "readme");

        var cleared = PluginTriggerKeywordMigration.TakeUnusable(settings, Candidates(("plugin", "Plugin", "SomeLabel", ConfigFieldValidation.None)));

        Assert.IsEmpty(cleared);
        Assert.AreEqual(PrecisionTrigger + "readme", settings.GetPluginSetting<string?>("plugin", "SomeLabel", null));
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
        var settings = WithKeyword("plugin", "TriggerKeyword", PrecisionTrigger + "calc");
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
