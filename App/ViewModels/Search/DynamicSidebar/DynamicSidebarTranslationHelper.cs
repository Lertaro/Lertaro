using System.Collections.ObjectModel;
using Lertaro.App.Services.Plugin;

namespace Lertaro.App.ViewModels.Search.DynamicSidebar;

// Split out purely to keep SearchViewModel under the repository's per-file line limit. This helper has
// no state of its own; it updates the sidebar groups owned by the one view model that calls it.
internal static class DynamicSidebarTranslationHelper
{
    // SidebarFilterGroup has no stable ID, so translated definitions are correlated by position. A
    // count mismatch means plugins changed mid-session; skip it rather than relabel the wrong item.
    public static void Refresh(ObservableCollection<DynamicSidebarGroupViewModel> groups)
    {
        var freshGroups = PluginManager.Instance.SidebarFilterProviders
            .SelectMany(provider => provider.GetFilterGroups())
            .ToList();

        if (freshGroups.Count != groups.Count)
            return;

        for (var i = 0; i < groups.Count; i++)
        {
            var group = groups[i];
            var fresh = freshGroups[i];
            group.UpdateHeader(fresh.Header);
            if (fresh.Items.Count != group.Items.Count)
                continue;

            for (var j = 0; j < group.Items.Count; j++)
                group.Items[j].UpdateDisplayName(fresh.Items[j].DisplayName);
        }
    }
}
