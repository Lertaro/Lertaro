using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.Translator;

public sealed class TranslatorPlugin : IPlugin, IConfigurable
{
    public string Name => TranslationService.Get("Translator_PluginName");
    public string Description => TranslationService.Get("Translator_PluginDesc");

    public PluginConfigSchema GetConfigSchema() => new()
    {
        Fields = new List<PluginConfigField>
        {
            new()
            {
                Key = "TranslationTrigger",
                LabelKey = "Translator_Config_TriggerLabel",
                DescriptionKey = "Translator_Config_TriggerDesc",
                FieldType = ConfigFieldType.Text,
                DefaultValue = "tr"
            }
        }
    };
}
