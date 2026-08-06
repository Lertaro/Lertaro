using Lertaro.Core;

namespace Lertaro.App.ViewModels.QuickPanel;

// Which tabs the strip has, and in what order. Split out of QuickPanelViewModelLoading.cs purely to keep
// that file under the repo's per-file line limit; this is the part of a refresh that decides what the
// strip IS, before anything is loaded into it.
public partial class QuickPanelViewModel
{
    /// <summary>The strip's contents in order: the user's workspaces and the plugins' tabs, interleaved.</summary>
    /// <remarks>
    /// One order over both kinds, resolved by the same helper the groups use -- an id nobody has ordered
    /// yet keeps its discovery position rather than jumping to the front. Workspaces come first in that
    /// discovery order, which is what a fresh install sees before anyone has dragged anything: the
    /// folders you set up, then whatever your plugins offer.
    /// </remarks>
    private List<IQuickPanelTabSource> OrderedTabs(QuickPanelSettings settings, List<QuickPanelTab> workspaces)
    {
        var sources = new Dictionary<string, IQuickPanelTabSource>(StringComparer.OrdinalIgnoreCase);
        foreach (var workspace in workspaces)
            sources[workspace.Id] = new WorkspaceTabSource(this, workspace);
        foreach (var provider in QuickPanelPluginTabs.Available)
        {
            var tab = new PluginTabSource(this, provider);
            sources[tab.Id] = tab;
        }

        return QuickPanelGroupOrdering
            .Resolve(sources.Keys, settings.TabOrder, settings.ClosedPluginTabIds)
            .Select(id => sources[id])
            .ToList();
    }

    /// <summary>Loads one workspace's visible folders, each on its own, and files each as it lands.</summary>
}
