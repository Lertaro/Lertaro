using Lertaro.App.ViewModels.QuickPanel;
using Lertaro.Core;
using Lertaro.PluginSdk.Abstractions.Plugins;

namespace Lertaro.App.ViewModels.Search;

internal static class QuickLaunchSourceCatalog
{
    public const string ManualSourceId = "__builtin__::QuickLaunchItems";

    public static IReadOnlyList<IQuickPanelTabProvider> Providers => QuickPanelPluginTabs.Available.ToList();

    public static string GetId(IQuickPanelTabProvider provider) => QuickPanelPluginTabs.ComponentId(provider);

    public static IQuickPanelTabProvider? Find(string id) => QuickPanelPluginTabs.Find(id);

    public static List<string> GetDefaultSourceIds()
        => GetDefaultSourceIds(Providers);

    internal static List<string> GetDefaultSourceIds(IEnumerable<IQuickPanelTabProvider> providers)
        => providers.Select(GetId).ToList();

    public static IReadOnlyList<string> GetEffectiveSourceIds(QuickLaunchSettings settings)
        => settings.SourceSelectionInitialized ? settings.EnabledSourceIds : GetDefaultSourceIds();
}
