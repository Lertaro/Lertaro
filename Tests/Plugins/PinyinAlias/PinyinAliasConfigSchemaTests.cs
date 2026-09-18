using Lertaro.PluginSdk.Abstractions;

namespace Lertaro.Plugins.PinyinAlias.Tests;

// The schema is what the Settings UI persists the toggle under, so its key/type/default are a contract
// with the setting the provider reads at query time -- asserted rather than assumed, because a rename on
// either side would silently leave the toggle writing to a key nothing reads.
[TestClass]
public sealed class PinyinAliasConfigSchemaTests
{
    [TestMethod]
    public void GetConfigSchema_SingleBooleanField_DefaultsToEnabled()
    {
        var schema = new PinyinAliasProvider().GetConfigSchema();

        var field = schema.Fields.Single();
        Assert.AreEqual(PinyinAliasConfigSchema.ConvertSettingKey, field.Key);
        Assert.AreEqual(ConfigFieldType.Boolean, field.FieldType);
        Assert.IsTrue((bool)field.DefaultValue);
    }

    [TestMethod]
    public void GetConfigSchema_FieldReferencesTranslationKeys()
    {
        var field = new PinyinAliasProvider().GetConfigSchema().Fields.Single();

        Assert.AreEqual("Plugins_PinyinAlias_ConvertTraditionalToSimplified", field.LabelKey);
        Assert.AreEqual("Plugins_PinyinAlias_ConvertTraditionalToSimplifiedDesc", field.DescriptionKey);
    }

    [TestMethod]
    public void PluginId_MatchesTheDllNameTheHostDerivesItFrom()
    {
        // The settings UI persists the field under this id, so a rename here without renaming the
        // assembly (or the other way round) would silently disconnect the toggle from the provider.
        Assert.AreEqual(
            PinyinAliasConfigSchema.PluginId,
            System.IO.Path.GetFileNameWithoutExtension(typeof(PinyinAliasProvider).Assembly.Location));
    }
}
