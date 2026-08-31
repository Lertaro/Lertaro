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

    internal static List<string> OrderSourceIds(IEnumerable<string> available, IEnumerable<string>? order)
        => QuickPanelGroupOrdering.Resolve(available, order, disabled: null);

    internal static List<LaunchPanelSourceViewModel> OrderSources(
        IEnumerable<LaunchPanelSourceViewModel> sources, IEnumerable<string>? order)
    {
        var available = sources.ToList();
        var byId = available.ToDictionary(source => source.Id, StringComparer.OrdinalIgnoreCase);
        return OrderSourceIds(available.Select(source => source.Id), order)
            .Select(id => byId[id])
            .ToList();
    }

    public static IReadOnlyList<string> GetEnabledSourceIds(QuickLaunchSettings settings)
        => GetEnabledSourceIds(settings, Providers);

    internal static List<string> GetEnabledSourceIds(QuickLaunchSettings settings,
        IEnumerable<IQuickPanelTabProvider> providers)
        => providers
            .Where(provider => !settings.DisabledSourceIds.Contains(GetId(provider), StringComparer.OrdinalIgnoreCase))
            .Select(GetId)
            .ToList();
}
