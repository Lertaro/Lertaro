using Lertaro.PluginSdk.Abstractions;

namespace Lertaro.Plugins.PinyinAlias;

// This assembly's one configurable surface, split out of PinyinAliasProvider.cs (composition, not a
// partial class) purely to keep that file under the repo's per-file line limit. The host resolves at
// most one IConfigurable per assembly (see PluginLoaderHelper.ResolveConfigurable), and it persists the
// fields under the id derived from the assembly's file name -- so declaring the schema here, with the
// plugin id owned next to the key the provider actually reads, is what keeps the two from drifting.
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
