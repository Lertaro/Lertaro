using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.OneCommander;

public class OneCommanderPlugin : IPlugin, IConfigurable, ITranslationProvider
{
    public string Name => "OneCommander";
    public string Description => TranslationService.Get("OneCommander_PluginDesc");

    public IReadOnlyList<string> SupportedCultures => TranslationService.GetSupportedCultures(System.Reflection.Assembly.GetExecutingAssembly());

    private static readonly Dictionary<string, Dictionary<string, string>> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object LockObj = new();

    public IReadOnlyDictionary<string, string> GetTranslations(string cultureName)
    {
        lock (LockObj)
        {
            if (Cache.TryGetValue(cultureName, out var cached))
            {
                return cached;
            }

            var translations = TranslationService.LoadEmbeddedTranslations(System.Reflection.Assembly.GetExecutingAssembly(), cultureName, "Plugin");
            Cache[cultureName] = translations;
            return translations;
        }
    }

    public PluginConfigSchema GetConfigSchema() => new PluginConfigSchema
    {
        Fields = new List<PluginConfigField>
        {
            new PluginConfigField
            {
                Key = "EnableInlineSearch",
                LabelKey = "Plugins_OneCommander_EnableInlineSearch",
                DescriptionKey = "Plugins_OneCommander_EnableInlineSearchDesc",
                FieldType = ConfigFieldType.Boolean,
                DefaultValue = true
            },
            new PluginConfigField
            {
                Key = "EnableQuickNav",
                LabelKey = "Plugins_OneCommander_EnableQuickNav",
                DescriptionKey = "Plugins_OneCommander_EnableQuickNavDesc",
                FieldType = ConfigFieldType.Boolean,
                DefaultValue = true
            }
        }
    };
}
