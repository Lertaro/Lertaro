using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Services;
using Lertaro.Plugins.QuickLookBridge.Actions;

namespace Lertaro.Plugins.QuickLookBridge;

// QuickLookPreviewProvider (IFilePreviewProvider) and QuickLookBridgeTranslationProvider
// (ITranslationProvider) are auto-discovered by the plugin loader scanning this assembly for their
// interfaces -- only the action needs explicit registration here, via IActionProvider.
public class QuickLookBridgePlugin : IPlugin, IActionProvider
{
    public string Name => TranslationService.Get("QuickLookBridge_PluginName");
    public string Description => TranslationService.Get("QuickLookBridge_PluginDesc");

    public IEnumerable<ISearchResultAction> GetActions() => new ISearchResultAction[]
    {
        new PreviewInQuickLookAction()
    };

    public IEnumerable<IDynamicActionProvider> GetDynamicActionProviders() => Array.Empty<IDynamicActionProvider>();
}
