using Lertaro.App.ViewModels.QuickPanel;
using Lertaro.Core;

namespace Lertaro.App.ViewModels.Settings.QuickPanel;

/// <summary>The plugin tabs the settings page lists, and whether each is currently in the strip.</summary>
/// <remarks>
/// A thin read over QuickPanelPluginTabs so the settings side never talks to PluginManager directly.
/// Read fresh each time the page is built rather than held: plugins can be enabled or disabled on their
/// own page, and a stale list would offer a tab that is no longer there or hide one that just appeared.
/// </remarks>
internal static class QuickPanelPluginTabCatalog
{
    /// <summary>Every plugin tab that exists right now, each saying whether the strip shows it.</summary>
    /// <remarks>
    /// Returned in the strip's own order rather than the plugins' discovery order, so the list on screen
    /// matches what the panel shows and a move made here starts from where the user last left it. The
    /// ordering helper is the same one the panel resolves its strip with; two answers to "what order are
    /// these tabs in" is how one of them ends up stale.
    /// </remarks>
    public static List<QuickPanelPluginTabOption> Available(QuickPanelSettings settings)
    {
        var options = QuickPanelPluginTabs.Available
            .Select(provider =>
            {
                var id = QuickPanelPluginTabs.ComponentId(provider);
                return new QuickPanelPluginTabOption(
                    id,
                    // The provider, not its Name: asking it again is what makes a language switch land.
                    () => provider.Name,
                    !settings.ClosedPluginTabIds.Contains(id, StringComparer.OrdinalIgnoreCase),
                    settings.ListViewPluginTabIds.Contains(id, StringComparer.OrdinalIgnoreCase));
            })
            .ToDictionary(option => option.Id, StringComparer.OrdinalIgnoreCase);

        return QuickPanelGroupOrdering
            .Resolve(options.Keys, settings.TabOrder, disabled: null)
            .Select(id => options[id])
            .ToList();
    }
}
