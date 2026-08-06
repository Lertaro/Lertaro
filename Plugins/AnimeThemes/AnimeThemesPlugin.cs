using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.AnimeThemes;

public class AnimeThemesPlugin : IPlugin
{
    public string Name => TranslationService.Get("AnimeThemes_PluginName");
    public string Description => TranslationService.Get("AnimeThemes_PluginDesc");
}
