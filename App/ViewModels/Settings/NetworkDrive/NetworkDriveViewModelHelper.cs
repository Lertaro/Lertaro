using Lertaro.App.Services;

using Lertaro.Core.Services.Search;

namespace Lertaro.App.ViewModels.Settings.NetworkDrive;

// Shape common to NetworkDriveSettingsItem/WslSettingsItem/FolderIndexSettingsItem -- lets
// RunRowAction/RebuildCategory below act on any of the three without duplicating their bodies per
// category. Implemented implicitly: each item class already had every one of these members before
// this interface existed, just not declared as implementing anything.
internal interface INetworkRowItem
{
    bool AppliedEnabled { get; set; }
    bool CanRunRowAction { get; set; }
    NetworkDriveRowAction RowAction { get; set; }
    string State { get; set; }
    string ItemCount { get; set; }
    bool IsPresent { get; set; }
    bool IsEnabled { get; set; }
    bool CanEditEnabled { get; set; }
    bool CanEditRefreshMode { get; set; }
}

internal static class NetworkDriveViewModelHelper
{
    // Split out of NetworkDriveSettingsViewModel to keep that file under the line-count limit.
    // Each of these three category-scoped rebuilds acts only on rows that are already AppliedEnabled (i.e.
    // actually saved in UserSettings already) -- never on whatever happens to be checked live in the UI.
    // These used to persist all three categories' live checkbox state before scanning, which meant clicking
    // "Rebuild" silently applied (and started indexing) a drive/distro/folder someone had just added or
    // re-checked but never confirmed via the window's own Apply/OK.
    public static void RebuildDrives(NetworkDriveSettingsViewModel vm, SearchService searchService, Action? onTriggerFastRefresh) =>
        RebuildCategory(vm.CanRebuildDrives, v => vm.CanRebuildDrives = v, v => vm.NetworkIndexSummary = v, searchService, vm.NetworkDrives, d => d.Drive, onTriggerFastRefresh);

    public static void RebuildWsl(NetworkDriveSettingsViewModel vm, SearchService searchService, Action? onTriggerFastRefresh) =>
        RebuildCategory(vm.CanRebuildWsl, v => vm.CanRebuildWsl = v, v => vm.WslIndexSummary = v, searchService, vm.WslDrives, w => w.UncPath, onTriggerFastRefresh);

    public static void RebuildFolders(NetworkDriveSettingsViewModel vm, SearchService searchService, Action? onTriggerFastRefresh) =>
        RebuildCategory(vm.CanRebuildFolders, v => vm.CanRebuildFolders = v, v => vm.FolderIndexSummary = v, searchService, vm.FolderIndexes, f => f.Path, onTriggerFastRefresh);

    private static void RebuildCategory<TItem>(
        bool canRebuild, Action<bool> setCanRebuild, Action<string> setSummary,
        SearchService searchService, IEnumerable<TItem> items, Func<TItem, string> getKey, Action? onTriggerFastRefresh)
        where TItem : INetworkRowItem
    {
        if (!canRebuild) return;

        setCanRebuild(false);
        setSummary(TranslationManager.Instance["Network_Rebuilding"]);
        searchService.ConfigureNetworkIndexes();
        foreach (var item in items)
        {
            if (item.AppliedEnabled)
                searchService.RefreshNetworkDriveIndex(getKey(item));
        }
        onTriggerFastRefresh?.Invoke();
    }

    public static void RunDriveAction(
        NetworkDriveSettingsItem item, NetworkDriveSettingsViewModel vm, SearchService searchService,
        Action onTriggerFastRefresh, HashSet<string> pendingRowRebuilds, HashSet<string> observedRowRebuilds) =>
        RunRowAction(item, item.Drive, searchService, onTriggerFastRefresh, pendingRowRebuilds, observedRowRebuilds,
            v => vm.NetworkIndexSummary = v, ResetAfterDelete);

    public static void RunWslDriveAction(
        WslSettingsItem item, NetworkDriveSettingsViewModel vm, SearchService searchService,
        Action onTriggerFastRefresh, HashSet<string> pendingRowRebuilds, HashSet<string> observedRowRebuilds) =>
        RunRowAction(item, item.UncPath, searchService, onTriggerFastRefresh, pendingRowRebuilds, observedRowRebuilds,
            v => vm.WslIndexSummary = v, ResetAfterDelete);

    public static void RunFolderIndexAction(
        FolderIndexSettingsItem item, NetworkDriveSettingsViewModel vm, SearchService searchService,
        Action onTriggerFastRefresh, HashSet<string> pendingRowRebuilds, HashSet<string> observedRowRebuilds) =>
        RunRowAction(item, item.Path, searchService, onTriggerFastRefresh, pendingRowRebuilds, observedRowRebuilds,
            v => vm.FolderIndexSummary = v, deleted =>
            {
                // Unlike a drive/WSL row (which stays visible as "an OS-resolvable thing you're just not
                // indexing" even after Delete), a folder row only exists because the user explicitly added
                // it -- there's no other reason to keep showing it once it's unchecked, whether or not it
                // ever got far enough to have a cache. Remove it from the list entirely instead of just
                // resetting its RowAction, and forget any pending-rebuild bookkeeping for it too.
                pendingRowRebuilds.Remove(item.Path);
                observedRowRebuilds.Remove(item.Path);
                vm.RemoveFolderIndex(deleted);
            });

    // A drive/WSL row stays visible after Delete (it's still "an OS-resolvable thing you're just not
    // indexing"), so it resets back to a clean unindexed state instead of disappearing.
    private static void ResetAfterDelete<TItem>(TItem item) where TItem : INetworkRowItem
    {
        item.RowAction = NetworkDriveRowAction.None;
        item.State = item.IsPresent ? TranslationManager.Instance["Network_StatusConnected"] : TranslationManager.Instance["Network_StatusUnavailable"];
        item.ItemCount = "-";
        item.CanRunRowAction = false;
        item.CanEditEnabled = item.IsPresent;
        item.CanEditRefreshMode = item.IsPresent;
        if (!item.IsPresent)
            item.IsEnabled = false;
    }

    private static void RunRowAction<TItem>(
        TItem item, string key, SearchService searchService, Action onTriggerFastRefresh,
        HashSet<string> pendingRowRebuilds, HashSet<string> observedRowRebuilds,
        Action<string> setRebuildingSummary, Action<TItem> onDeleted)
        where TItem : INetworkRowItem
    {
        if (!item.CanRunRowAction)
            return;

        item.CanRunRowAction = false;
        if (item.RowAction == NetworkDriveRowAction.Rebuild)
        {
            // RowAction == Rebuild only ever shows for a row that's already AppliedEnabled (see
            // NetworkDriveSettingsViewModel.UpdateRowAction), so it's already correctly saved -- no
            // need to re-persist anything here.
            item.State = TranslationManager.Instance["Network_StatusIndexing"];
            item.ItemCount = "-";
            setRebuildingSummary(TranslationManager.Instance["Network_Rebuilding"]);
            pendingRowRebuilds.Add(key);
            if (!searchService.RefreshNetworkDriveIndex(key))
            {
                pendingRowRebuilds.Remove(key);
                observedRowRebuilds.Remove(key);
            }
        }
        else if (item.RowAction == NetworkDriveRowAction.Delete)
        {
            searchService.DeleteNetworkDriveCache(key);
            onDeleted(item);
        }
        else if (item.RowAction == NetworkDriveRowAction.Stop)
        {
            // Don't touch item.State/RowAction here -- the next status poll (RefreshNetworkDrives) will
            // pick up whatever Scheduler.CancelDrive actually settles on and re-derive both correctly.
            pendingRowRebuilds.Remove(key);
            observedRowRebuilds.Remove(key);
            searchService.CancelNetworkDriveIndex(key);
        }
        onTriggerFastRefresh?.Invoke();
    }
}
