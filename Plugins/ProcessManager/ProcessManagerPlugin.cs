using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.ProcessManager;

public class ProcessManagerPlugin : IPlugin, IConfigurable
{
    public string Name => TranslationService.Get("ProcessManager_PluginName");
    public string Description => TranslationService.Get("ProcessManager_PluginDesc");

    public PluginConfigSchema GetConfigSchema() => new PluginConfigSchema
    {
        Fields = new List<PluginConfigField>
        {
            new PluginConfigField
            {
                Key = "TriggerKeyword",
                LabelKey = "ProcessManager_Config_TriggerKeywordLabel",
                DescriptionKey = "ProcessManager_Config_TriggerKeywordDesc",
                FieldType = ConfigFieldType.Text,
                DefaultValue = "ps",
                RequireNonEmpty = true
            }
        }
    };
}
