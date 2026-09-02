using Lertaro.Core;
using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.App.Services.Plugin;

namespace Lertaro.App.ViewModels.QuickPanel;

// Loading a plugin's tab. Split out of QuickPanelViewModelLoading.cs purely to keep that file under the
// repo's per-file line limit; it is the one part of the load that never goes near a folder.
public partial class QuickPanelViewModel
{
    /// <summary>Asks a provider for its list and files it into groups.</summary>
    /// <remarks>
    /// Groups entries by parent directory so a plugin tab can contain multiple distinct groups.
    /// Header toggles and titles are displayed per group when paths exist.
    /// </remarks>
    internal async Task LoadPluginTabAsync(
        IQuickPanelTabProvider provider,
        string id,
        string name,
        Action<QuickPanelGroupViewModel, int> place,
        CancellationToken token)
    {
        IReadOnlyList<PluginSdk.Abstractions.ISearchResult> entries;
        try
        {
            entries = await PluginPerformanceMonitor.MeasureAsync(provider, () => provider.GetEntriesAsync(token)).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // A provider that throws costs its own tab and nothing else, exactly as an unreachable folder
            // costs its own group.
            Logger.Log($"[QuickPanel] Plugin tab '{id}' failed: {ex.Message}", LogLevel.Error);
            return;
        }

        if (entries.Count == 0) return;

        // Mapped rather than cast: a provider lives in a plugin assembly that cannot see AppSearchResult
        // at all, so what arrives is always some ISearchResult of its own making.
        var items = entries
            .Select((entry, index) => (
                Item: Helpers.PluginResultMapper.ToUiResult(entry, index),
                Modified: entry.Metadata.Modified is var modified && modified != DateTime.MinValue ? modified : (DateTime?)null))
            .ToList();

        // Tiles unless the settings page says otherwise, which is what a folder source gets too. The
        // header's own toggle still overrides it for as long as the panel is open.
        var asList = _readSettings().ListViewPluginTabIds.Contains(id, StringComparer.OrdinalIgnoreCase);

        // Group entries by parent directory so a plugin tab can contain multiple groups/folders.
        var groups = items
            .GroupBy(i => i.Item.ParentDir ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var singleGroupNoPath = groups.Count == 1 && string.IsNullOrEmpty(groups[0].Key);

        for (var rank = 0; rank < groups.Count; rank++)
        {
            var group = groups[rank];
            var dirPath = group.Key;
            var groupTitle = !string.IsNullOrEmpty(dirPath)
                ? (System.IO.Path.GetFileName(dirPath.TrimEnd('\\', '/')) is { Length: > 0 } folderName ? folderName : dirPath)
                : name;

            // Avoid duplicating the text on the right when title is already identical to dirPath (e.g. "D:\").
            var displayFolderPath = string.Equals(groupTitle, dirPath, StringComparison.OrdinalIgnoreCase)
                ? string.Empty
                : dirPath;

            var groupViewModel = new QuickPanelGroupViewModel(
                $"{id}:{rank}",
                groupTitle,
                displayFolderPath,
                group.ToList(),
                QuickPanelSortMode.ModifiedDescending,
                thumbnailView: !asList,
                expanded: true,
                acceptsDrops: false,
                showsHeading: !singleGroupNoPath);

            place(groupViewModel, rank);
        }
    }
}
