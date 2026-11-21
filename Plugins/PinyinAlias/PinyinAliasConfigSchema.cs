using Lertaro.PluginSdk.Abstractions;

namespace Lertaro.Plugins.PinyinAlias;

// Defines this assembly's IConfigurable schema and shared plugin ID. The host accepts at most one schema
// per assembly and stores values under its assembly-derived ID.
internal static class PinyinAliasConfigSchema
{
    // The id the host derives from this assembly's file name, which is also what the Settings UI
    // persists a config field under (see PluginLoaderHelper.TryLoadConfigFields).
    internal const string PluginId = "Lertaro.Plugins.PinyinAlias";

    // On by default: a Traditional query reaching Simplified names (and the reverse) is what a Chinese
    // user expects from search, and the conversion itself is a single locale call.
    internal const string ConvertSettingKey = "ConvertTraditionalToSimplified";

    internal static PluginConfigSchema Create() => new()
    {
        Fields = new List<PluginConfigField>
        {
            new PluginConfigField
            {
                Key = ConvertSettingKey,
                LabelKey = "Plugins_PinyinAlias_ConvertTraditionalToSimplified",
                DescriptionKey = "Plugins_PinyinAlias_ConvertTraditionalToSimplifiedDesc",
                FieldType = ConfigFieldType.Boolean,
                DefaultValue = true
            }
        }
    };
}
