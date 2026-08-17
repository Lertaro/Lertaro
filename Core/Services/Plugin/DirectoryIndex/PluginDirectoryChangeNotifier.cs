using Lertaro.Core.DriveMonitoring;
using Lertaro.Core.Services.Network;
using Lertaro.Core.Services.Search;

namespace Lertaro.Core.Services.Plugin.DirectoryIndex;

/// <summary>
/// Turns "something under a plugin's registered directory changed" into at most one notification per
/// quiet period, from two kinds of source at once: the per-directory FileSystemWatcher this registry
/// already runs, and the indexes themselves reporting that they just took an update in.
/// <para>
/// The index half exists because a watcher event and the index are not in step: a watcher fires the
/// instant the filesystem changes, while the USN journal is read on a poll, so a plugin that re-listed
/// immediately could read an index that has not caught up yet and miss the file that triggered it. An
/// index signal cannot be early by construction -- it IS the index having changed -- and the debounce
/// below settles the two into one refresh. It also covers a stretch where the watcher itself was down
/// (buffer overflow, share disconnected), which today is only noticed on its next reconnect.
/// </para>
/// <para>
/// The watcher half stays because it is the only signal for a directory no index covers -- a drive
/// indexing is off for, an unconfigured share, a path that does not exist yet. Everywhere else the
/// index sees every kind of change, edits to a file's contents included: those refresh its size and
/// timestamps (see UsnIndexerExtensions' metadata pass) and bump the drive's revision like any other.
/// </para>
/// </summary>
internal sealed class PluginDirectoryChangeNotifier : IDisposable
{
    // Long enough to outlast the USN monitor's own poll (200ms-1s, see UsnMonitor) so a watcher event
    // and the index update it will produce collapse into ONE notification, taken after the index has
    // caught up rather than before. Each new event restarts it, so a bulk copy notifies once, at the
    // end, instead of once per file.
    private const int QuietPeriodMs = 1200;

    private readonly KeyedDebouncer<string> _debouncer = new(QuietPeriodMs, StringComparer.OrdinalIgnoreCase);
    private readonly Func<IReadOnlyList<(string PluginId, string Path)>> _registrations;
    private readonly object _gate = new();
    private CancellationTokenSource? _localStatusCts;
    private bool _subscribedToNetwork;

    public PluginDirectoryChangeNotifier(Func<IReadOnlyList<(string PluginId, string Path)>> registrations)
        => _registrations = registrations;

    /// <summary>One notification for this plugin once its directories have been quiet for a moment.</summary>
    public void Report(string pluginId)
        => _debouncer.Schedule(pluginId, () => PluginSdk.Services.DirectoryIndexerService.NotifyDirectoryChanged(pluginId));

    /// <summary>
    /// Starts listening to the indexes, if not already. Called when a directory is registered rather
    /// than from the constructor: with nothing registered there is nobody to notify, and the local half
    /// holds a pipe subscription open for as long as it runs.
    /// </summary>
    public bool EnsureIndexSubscriptions()
    {
        var startedLocalSubscription = false;
        lock (_gate)
        {
            if (!_subscribedToNetwork)
            {
                // Network drives, WSL distros and folder indexes all publish through here when their
                // index takes an update (scan, checkpoint, watcher-driven incremental publish).
                UserNetworkDriveSearch.DirectoriesChanged += OnNetworkDirectoriesChanged;
                _subscribedToNetwork = true;
            }
            if (_localStatusCts == null)
            {
                var subscription = new CancellationTokenSource();
                _localStatusCts = subscription;
                var token = subscription.Token;
                _ = Task.Run(() => WatchLocalIndexAsync(token));
                startedLocalSubscription = true;
            }
        }
        return startedLocalSubscription;
    }

    /// <summary>Reconnects the local pipe with the current registration set as its watch list.</summary>
    public void RefreshLocalIndexSubscription()
    {
        lock (_gate)
        {
            if (_localStatusCts == null)
                return;

            _localStatusCts.Cancel();
            _localStatusCts.Dispose();
            var subscription = new CancellationTokenSource();
            _localStatusCts = subscription;
            var token = subscription.Token;
            _ = Task.Run(() => WatchLocalIndexAsync(token));
        }
    }

    /// <summary>Stops listening once nothing is registered any more.</summary>
    public void StopIndexSubscriptions()
    {
        lock (_gate)
        {
            if (_subscribedToNetwork)
            {
                UserNetworkDriveSearch.DirectoriesChanged -= OnNetworkDirectoriesChanged;
                _subscribedToNetwork = false;
            }
            _localStatusCts?.Cancel();
            _localStatusCts?.Dispose();
            _localStatusCts = null;
        }
    }

    // Network shares, WSL distros and folder indexes, the same shape as a local drive but without the
    // pipe in between: these indexes are built and held in THIS process, so their changes arrive as a
    // plain event and are matched here rather than over a subscription.
    private void OnNetworkDirectoriesChanged(string drive, IReadOnlyCollection<string>? changedDirectories)
    {
        var registrations = _registrations();
        var watched = registrations.Select(r => r.Path).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        OnWatchedDirectoriesChanged(WatchedDirectoryMatcher.Match(watched, changedDirectories));
    }

    // Holds the subscription open, re-establishing it whenever the service goes away (an upgrade, a
    // manual restart). The watch list is sent with the subscribe, so registration changes explicitly
    // replace this loop through RefreshLocalIndexSubscription instead of leaving the pipe stale.
    private async Task WatchLocalIndexAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                var watched = _registrations().Select(r => r.Path).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                await DirectoryChangeStream.SubscribeAsync(watched, OnWatchedDirectoriesChanged, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                Logger.Log($"[IndexManager] Index change subscription dropped, retrying: {ex.Message}", LogLevel.Debug);
            }

            // The service being down/restarting is routine (upgrade, manual restart); the per-directory
            // watchers keep working meanwhile, so this only has to come back eventually.
            try
            {
                await Task.Delay(5000, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    // One message per hit, and only for directories this process actually asked about. The matching
    // happens on the service's side now (see SearchRequestId.SubscribeDirectoryChanges): changes arrive
    // there at roughly 3000 batches a second on an ordinary working C:, and no summary small enough to
    // ship with a status covers the window between two of them -- which is why every change used to
    // read as "somewhere on this drive" and re-list every plugin's directories.
    private void OnWatchedDirectoriesChanged(IReadOnlyList<string> changed)
    {
        var registrations = _registrations();
        var reported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var directory in changed)
        {
            foreach (var (pluginId, path) in registrations)
            {
                if (WatchedDirectoryMatcher.Touches(directory, path) && reported.Add(pluginId))
                    Report(pluginId);
            }
        }
    }

    public void Dispose()
    {
        StopIndexSubscriptions();
        _debouncer.Dispose();
    }
}
