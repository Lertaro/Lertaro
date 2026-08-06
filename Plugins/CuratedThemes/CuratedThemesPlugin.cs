using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.CuratedThemes;

public class CuratedThemesPlugin : IPlugin
{
    public string Name => TranslationService.Get("CuratedThemes_PluginName");
    public string Description => TranslationService.Get("CuratedThemes_PluginDesc");
}
