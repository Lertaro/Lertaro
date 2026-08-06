using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.WindowSwitcher;

public class WindowSwitcherPlugin : IPlugin, IConfigurable
{
    public string Name => TranslationService.Get("WindowSwitcher_PluginName");
    public string Description => TranslationService.Get("WindowSwitcher_PluginDesc");

    public PluginConfigSchema GetConfigSchema() => new PluginConfigSchema
    {
        Fields = new List<PluginConfigField>
        {
            new PluginConfigField
            {
                Key = "TriggerKeyword",
                LabelKey = "WindowSwitcher_Config_TriggerKeywordLabel",
                DescriptionKey = "WindowSwitcher_Config_TriggerKeywordDesc",
                FieldType = ConfigFieldType.Text,
                DefaultValue = "win",
                RequireNonEmpty = true
            },
            new PluginConfigField
            {
                Key = "UseScreenshotIcons",
                LabelKey = "WindowSwitcher_Config_UseScreenshotIconsLabel",
                DescriptionKey = "WindowSwitcher_Config_UseScreenshotIconsDesc",
                FieldType = ConfigFieldType.Boolean,
                DefaultValue = true
            }
        }
    };
}
