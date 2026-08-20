using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.AutoCAD;

public sealed class AutoCADPlugin : IPlugin, ITranslationProvider
{
    public string Name => "AutoCAD";

    public string Description => TranslationService.Get("AutoCAD_PluginDescription");

    public IReadOnlyList<string> SupportedCultures =>
        TranslationService.GetSupportedCultures(typeof(AutoCADPlugin).Assembly);

    public IReadOnlyDictionary<string, string> GetTranslations(string cultureName) =>
        TranslationService.LoadEmbeddedTranslations(typeof(AutoCADPlugin).Assembly, cultureName, "Plugin");
}
