using System.Diagnostics;
using System.IO;
using System.Windows.Threading;
using Lertaro.Core;
using Lertaro.Core.Services.Network;
using Lertaro.Core.Services.Plugin.DirectoryIndex;
using Lertaro.Core.Services.Search;
using Application = System.Windows.Application;

namespace Lertaro.App.ViewModels.Search;

/// <summary>
/// Re-runs a settled full-window query when a directory containing one of its rows changes.
/// </summary>
/// <remarks>
/// The index already receives rename/create/delete events. The missing link is the open result list:
/// it is a snapshot, so without this subscription its path and shell actions remain stale until the
/// next query.
/// <para>
/// The watch list is the set of directories holding the current rows, re-subscribed whenever that set
/// moves. Watching drive roots instead would be a fixed list, but the service matches a root against
/// every change on its drive, so each file operation in the system would be pushed to this window --
/// exactly the traffic the pipe subscription is designed to filter out. A re-subscribe costs one round
/// trip and only happens when the rows move to a different set of folders.
/// </para>
/// </remarks>
internal sealed class SearchResultRefreshWatcher : IDisposable
{
    // Merges the burst a single shell operation produces (deleting a folder arrives as one record per
    // child) while still reflecting a rename before the user reaches for the row.
    private static readonly TimeSpan RefreshDelay = TimeSpan.FromMilliseconds(600);

    // A directory that keeps changing (a download, a build) never goes quiet, so a deferred refresh is
    // forced once the burst has lasted this long instead of waiting for a gap that may not come.
    private static readonly TimeSpan MaxRefreshDelay = TimeSpan.FromSeconds(3);

    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);

    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _timer;
    private readonly Func<IReadOnlyList<AppSearchResult>> _getResults;
    private readonly Action _refresh;
    private readonly Func<bool> _isSearchRunning;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly object _subscriptionLock = new();

    // Replaced as a whole and never mutated afterwards, so the pipe thread may read it without a lock.
    // A replacement is only published by the newest update (see _updateGeneration), so a slow path walk
    // cannot overwrite the watch list of a query that has already replaced it.
    private volatile HashSet<string> _watchedDirectories = new(StringComparer.OrdinalIgnoreCase);

    // Guards the publish/cancel handshake against Dispose; the source is disposed by the loop that owns
    // it, never here.
    private CancellationTokenSource? _subscription;
    private volatile bool _disposed;
    private int _updateGeneration;
    private int _schedulePosted;

    // UI thread only: when the current burst of changes started, and whether a change arrived while a
    // search was running (the refresh then waits for it to settle instead of cancelling it).
    private long _burstStartedAt;
    private bool _refreshPending;

    public SearchResultRefreshWatcher(
        Func<IReadOnlyList<AppSearchResult>> getResults,
        Action refresh,
        Func<bool>? isSearchRunning = null,
        Dispatcher? dispatcher = null)
    {
        _getResults = getResults;
        _refresh = refresh;
        _isSearchRunning = isSearchRunning ?? (() => false);
        _dispatcher = dispatcher ?? Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        _timer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher) { Interval = RefreshDelay };
        _timer.Tick += OnTimerTick;

        UserNetworkDriveSearch.DirectoriesChanged += OnNetworkDirectoriesChanged;
    }

    /// <summary>Rebuilds the watched directories after the window shows a different set of rows.</summary>
    public void UpdateResults(IEnumerable<AppSearchResult> results)
    {
        if (_disposed)
            return;

        // A change that arrived while the previous search was running was parked here. Rerun it now that
        // the rows it matched are the ones on screen.
        if (_refreshPending && !_isSearchRunning())
        {
            _refreshPending = false;
            ScheduleRefresh();
        }

        // Only the strings are read here: a content query may hold a very large list, and parsing every
        // path on the UI thread would stall the render that just produced it.
        var paths = new List<string>(results is ICollection<AppSearchResult> collection ? collection.Count : 0);
        foreach (var result in results)
            paths.Add(result.FullPath);

        var generation = Interlocked.Increment(ref _updateGeneration);
        if (paths.Count == 0)
        {
            PublishWatchedDirectories(new HashSet<string>(StringComparer.OrdinalIgnoreCase), generation);
            return;
        }

        _ = Task.Run(() => PublishWatchedDirectories(ResolveWatchedDirectories(paths), generation));
    }

    internal static HashSet<string> ResolveWatchedDirectories(IEnumerable<AppSearchResult> results)
        => ResolveWatchedDirectories(results.Select(result => result.FullPath));

    internal static HashSet<string> ResolveWatchedDirectories(IEnumerable<string> paths)
    {
        var watched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            // Plugin rows carry sentinel paths ("__SHOW_MORE__" and friends) that no drive holds, and a
            // provider may report a bare "name:title" value that only looks like a drive-qualified path.
            if (string.IsNullOrWhiteSpace(path)
                || path.StartsWith("__", StringComparison.Ordinal)
                || (path.Contains(":", StringComparison.Ordinal) && !Path.IsPathFullyQualified(path)))
                continue;

            try
            {
                var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (string.Equals(Path.GetPathRoot(path)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                        trimmed, StringComparison.OrdinalIgnoreCase))
                    continue;

                var parent = Path.GetDirectoryName(trimmed);
                if (!string.IsNullOrWhiteSpace(parent))
                    watched.Add(parent);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
            {
                // Not a path this platform can parse; there is nothing to watch for it.
            }
        }

        return watched;
    }

    internal static bool ShouldRefresh(
        IReadOnlyCollection<string> changedDirectories,
        IReadOnlyCollection<string> watchedDirectories)
    {
        if (changedDirectories.Count == 0 || watchedDirectories.Count == 0)
            return false;

        foreach (var changed in changedDirectories)
        {
            if (string.IsNullOrWhiteSpace(changed))
                continue;

            var normalized = NormalizeDriveLetter(changed);
            if (IsRootPath(normalized, out var root))
            {
                if (watchedDirectories.Any(path => SameRoot(path, root)))
                    return true;
                continue;
            }

            if (watchedDirectories.Any(path => WatchedDirectoryMatcher.Touches(normalized, path)))
                return true;
        }

        return false;
    }

    private void PublishWatchedDirectories(HashSet<string> watched, int generation)
    {
        CancellationTokenSource? replaced = null;
        CancellationTokenSource? subscription = null;
        string[]? watchList = null;

        lock (_subscriptionLock)
        {
            // A result set a newer update already replaced would subscribe to the folders of a query the
            // window no longer shows.
            if (_disposed || generation != Volatile.Read(ref _updateGeneration))
                return;

            var previous = _watchedDirectories;
            _watchedDirectories = watched;
            if (previous.SetEquals(watched))
                return;

            replaced = _subscription;
            _subscription = null;

            if (watched.Count > 0)
            {
                subscription = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
                _subscription = subscription;
                watchList = watched.ToArray();
            }
        }

        replaced?.Cancel();

        if (subscription != null && watchList != null)
            _ = Task.Run(() => WatchDirectoriesAsync(watchList, subscription));
    }

    private async Task WatchDirectoriesAsync(string[] watchList, CancellationTokenSource subscription)
    {
        var token = subscription.Token;
        try
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await DirectoryChangeStream.SubscribeAsync(watchList, OnChangedDirectories, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    Logger.Log($"[SearchResultRefresh] Index change subscription dropped, retrying: {ex.Message}", LogLevel.Debug);
                }

                try
                {
                    await Task.Delay(RetryDelay, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
        finally
        {
            subscription.Dispose();
        }
    }

    private void OnChangedDirectories(IReadOnlyList<string> changed)
    {
        if (ShouldRefresh(changed, _watchedDirectories))
            ScheduleRefresh();
    }

    private void OnNetworkDirectoriesChanged(string drive, IReadOnlyCollection<string>? changedDirectories)
    {
        // A null list means the index could not narrow the change down to folders; treat the drive root as
        // changed so a matching row is not left stale. This over-refreshes, never under-refreshes.
        var changed = changedDirectories ?? new[] { NormalizeDriveLetter(drive) };
        if (ShouldRefresh(changed, _watchedDirectories))
            ScheduleRefresh();
    }

    private void ScheduleRefresh()
    {
        if (_disposed)
            return;

        // One queued hop is enough: changes arriving before it runs are merged into it, whereas posting
        // once per change would flood the dispatcher during a bulk delete or a build output.
        if (Interlocked.Exchange(ref _schedulePosted, 1) == 1)
            return;

        try
        {
            _dispatcher.BeginInvoke(new Action(OnChangePosted), DispatcherPriority.Background);
        }
        catch (InvalidOperationException)
        {
            // The dispatcher is already shutting down; there is no window left to refresh.
            Volatile.Write(ref _schedulePosted, 0);
        }
    }

    private void OnChangePosted()
    {
        Volatile.Write(ref _schedulePosted, 0);
        if (_disposed)
            return;

        // Debounce: every further change pushes the deadline out, so a burst is answered once when it
        // ends. The elapsed check is what stops a directory that never goes quiet from postponing the
        // refresh forever.
        var now = Stopwatch.GetTimestamp();
        if (_timer.IsEnabled)
            _timer.Stop();
        else
            _burstStartedAt = now;

        if (Stopwatch.GetElapsedTime(_burstStartedAt, now) >= MaxRefreshDelay)
        {
            RunRefresh();
            return;
        }

        _timer.Start();
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        _timer.Stop();
        RunRefresh();
    }

    // Internal so the deferral can be exercised without a live index subscription.
    internal void RunRefresh()
    {
        if (_disposed)
            return;

        // Refreshing means running the query again, and a query starts by cancelling the one in flight:
        // firing now would restart it from zero, and on a busy file system it could never settle. Defer to
        // the settled callback instead.
        if (_isSearchRunning())
        {
            _refreshPending = true;
            return;
        }

        _refreshPending = false;
        if (_getResults().Count > 0)
            _refresh();
    }

    private static string NormalizeDriveLetter(string path)
        => path.Length == 1 && char.IsLetter(path[0]) ? $"{path}:\\" : path;

    private static bool IsRootPath(string path, out string root)
    {
        root = Path.GetPathRoot(path) ?? string.Empty;
        if (root.Length == 0)
            return false;

        var trimmedRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var trimmedPath = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(trimmedRoot, trimmedPath, StringComparison.OrdinalIgnoreCase);
    }

    private static bool SameRoot(string path, string root)
        => string.Equals(
            Path.GetPathRoot(path)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);

    public void Dispose()
    {
        CancellationTokenSource? subscription = null;
        lock (_subscriptionLock)
        {
            if (_disposed)
                return;

            _disposed = true;
            subscription = _subscription;
            _subscription = null;
        }

        UserNetworkDriveSearch.DirectoriesChanged -= OnNetworkDirectoriesChanged;
        subscription?.Cancel();
        // Cancels any subscription still being created and unblocks the pipe read of the current one.
        _lifetime.Cancel();
        _lifetime.Dispose();
        _timer.Stop();
        _timer.Tick -= OnTimerTick;
    }
}
