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

    public static IReadOnlyList<string> GetEnabledSourceIds(QuickLaunchSettings settings)
        => GetEnabledSourceIds(settings, Providers);

    internal static List<string> GetEnabledSourceIds(QuickLaunchSettings settings,
        IEnumerable<IQuickPanelTabProvider> providers)
        => providers
            .Where(provider => !settings.DisabledSourceIds.Contains(GetId(provider), StringComparer.OrdinalIgnoreCase))
            .Select(GetId)
            .ToList();
}
