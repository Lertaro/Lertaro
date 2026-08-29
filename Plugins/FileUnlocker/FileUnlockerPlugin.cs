using System.Reflection;
using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Services;
using Lertaro.Plugins.FileUnlocker.Actions;

namespace Lertaro.Plugins.FileUnlocker;

public sealed class FileUnlockerPlugin : IPlugin, IActionProvider
{
    public string Name => TranslationService.Get("FileUnlocker_PluginName");

    public string Description => TranslationService.Get("FileUnlocker_PluginDesc");

    public IEnumerable<ISearchResultAction> GetActions() => [new ReleaseFileOccupationAction()];

    public IEnumerable<IDynamicActionProvider> GetDynamicActionProviders() => Array.Empty<IDynamicActionProvider>();
}

public sealed class FileUnlockerTranslationProvider : ITranslationProvider
{
    public string Name => "FileUnlocker Translation Provider";

    public IReadOnlyList<string> SupportedCultures => TranslationService.GetSupportedCultures(Assembly.GetExecutingAssembly());

    public IReadOnlyDictionary<string, string> GetTranslations(string cultureName) =>
        TranslationService.LoadEmbeddedTranslations(Assembly.GetExecutingAssembly(), cultureName, "Plugin");
}
