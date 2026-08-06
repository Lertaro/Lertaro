namespace Lertaro.App.ViewModels.QuickPanel;

// How the panel notices that a folder it is showing has changed. Split out of
// QuickPanelViewModelLoading.cs purely to keep that file under the repo's per-file line limit; it has no
// state beyond the registrations it holds while the panel is open.
public partial class QuickPanelViewModel
{
    // The registration keys currently held, one per group on screen. Empty means nothing is subscribed.
    private readonly List<string> _watchKeys = new();
    private readonly List<IDisposable> _watches = new();

    // Each source subscribes under its own key, so the notification names which group to reload rather
    // than "something the panel is showing changed". Prefixed to keep it clear of a plugin's own id,
    // which is what this registry is otherwise keyed by.
    private static string WatchKey(string sourceId) => "__quickpanel::" + sourceId;

    /// <summary>Starts noticing changes to the folders on screen. Called once the panel is up.</summary>
    /// <remarks>
    /// Through the directory registry rather than a watcher of the panel's own, which is the difference
    /// between duplicating the USN index and using it: the registry reports from BOTH the index taking
    /// an update in and a FileSystemWatcher, debounced together, with the watcher there for the
    /// directories no index covers (a share, a drive indexing is off for). A watcher on its own is early
    /// by construction -- it fires when the filesystem changes, while the panel reads the index, which
    /// has not caught up yet.
    ///
    /// Only the workspace being shown, not all of them: a folder behind a tab nobody is looking at can
    /// change all it likes, and that tab is reloaded when it is switched to anyway.
    /// </remarks>
    public void StartWatching()
    {
        StopWatching();

        // Touching Instance is what binds the SDK delegates this then calls through; without it the
        // registration below is a silent no-op.
        _ = Core.Services.Plugin.DirectoryIndex.CoreDirectoryIndexManager.Instance;

        foreach (var group in Groups)
        {
            var source = SourceOf(group.SourceId);
            if (source == null || string.IsNullOrEmpty(group.FolderPath)) continue;

            var key = WatchKey(group.SourceId);
            // Recursive to match the source: a source that lists its subfolders is changed by a write in
            // one of them, and one that does not is not.
            PluginSdk.Services.DirectoryIndexerService.RegisterDirectory(
                key, group.FolderPath, source.Recursive, "*");
            // One subscription per registration, each closing over the group it belongs to: what comes
            // back is "this group's folder changed", not "somebody's folder changed, was it yours".
            var sourceId = group.SourceId;
            _watches.Add(PluginSdk.Services.DirectoryIndexerService.WatchDirectories(key, () => ReloadGroup(sourceId)));
            _watchKeys.Add(key);
        }
    }

    /// <summary>Stops, and forgets, everything StartWatching set up. Safe to call when nothing is running.</summary>
    public void StopWatching()
    {
        foreach (var watch in _watches)
            watch.Dispose();
        _watches.Clear();

        foreach (var key in _watchKeys)
            PluginSdk.Services.DirectoryIndexerService.UnregisterDirectories(key);
        _watchKeys.Clear();
    }

    /// <summary>Re-aims the watchers at whatever is on screen now, but only if they were already running.</summary>
    /// <remarks>
    /// Switching workspace replaces every group, so watchers aimed at the old ones are watching folders
    /// nobody is looking at. Conditional, because this also runs during a refresh that happens before
    /// there is a window: starting unconditionally would leave watchers running for a panel that never
    /// opened.
    /// </remarks>
    private void RewatchIfWatching()
    {
        if (_watchKeys.Count > 0) StartWatching();
    }

    /// <summary>Reloads the one group whose folder settled. Raised off the UI thread, so it comes back.</summary>
    private void ReloadGroup(string sourceId)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher == null) return;

        dispatcher.BeginInvoke(new Action(() =>
        {
            var group = Groups.FirstOrDefault(g => g.SourceId.Equals(sourceId, StringComparison.OrdinalIgnoreCase));
            if (group != null) _ = ReloadGroupAsync(group);
        }));
    }
}
