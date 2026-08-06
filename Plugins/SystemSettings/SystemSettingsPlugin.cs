using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.SystemSettings;

public class SystemSettingsPlugin : IPlugin
{
    public string Name => TranslationService.Get("SystemSettings_PluginName");
    public string Description => TranslationService.Get("SystemSettings_PluginDesc");
}
