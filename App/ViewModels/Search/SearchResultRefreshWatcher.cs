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
/// next query. The local subscription watches drive roots and filters here to keep the service-side
/// watch list fixed; network indexes publish their changes through the in-process event.
/// </remarks>
internal sealed class SearchResultRefreshWatcher : IDisposable
{
    private static readonly string[] LocalRoots = Enumerable.Range('A', 26)
        .Select(letter => $"{(char)letter}:\\")
        .ToArray();

    private static readonly TimeSpan RefreshDelay = TimeSpan.FromMilliseconds(600);

    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _timer;
    private readonly Func<IReadOnlyList<AppSearchResult>> _getResults;
    private readonly Action _refresh;
    private readonly CancellationTokenSource _subscription = new();
    private volatile HashSet<string> _watchedDirectories = new(StringComparer.OrdinalIgnoreCase);
    private volatile bool _disposed;

    public SearchResultRefreshWatcher(
        Func<IReadOnlyList<AppSearchResult>> getResults,
        Action refresh,
        Dispatcher? dispatcher = null)
    {
        _getResults = getResults;
        _refresh = refresh;
        _dispatcher = dispatcher ?? Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        _timer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher) { Interval = RefreshDelay };
        _timer.Tick += OnTimerTick;

        UserNetworkDriveSearch.DirectoriesChanged += OnNetworkDirectoriesChanged;
        _ = Task.Run(() => WatchLocalDirectoriesAsync(_subscription.Token));
    }

    /// <summary>Rebuilds the parent-directory filter after a search has settled.</summary>
    public void UpdateResults(IEnumerable<AppSearchResult> results)
    {
        if (_disposed)
            return;

        _watchedDirectories = ResolveWatchedDirectories(results);
    }

    internal static HashSet<string> ResolveWatchedDirectories(IEnumerable<AppSearchResult> results)
    {
        var watched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var result in results)
        {
            var path = result.FullPath;
            if (string.IsNullOrWhiteSpace(path)
                || path.StartsWith("__", StringComparison.Ordinal)
                || path.Contains(":", StringComparison.Ordinal) && !Path.IsPathFullyQualified(path))
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
            catch (ArgumentException)
            {
                // Synthetic/provider rows can carry non-path values; they have nothing to watch.
            }
            catch (NotSupportedException)
            {
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

    private async Task WatchLocalDirectoriesAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await DirectoryChangeStream.SubscribeAsync(
                    LocalRoots,
                    changed => OnChangedDirectories(changed),
                    token).ConfigureAwait(false);
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
                await Task.Delay(TimeSpan.FromSeconds(5), token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private void OnChangedDirectories(IReadOnlyList<string> changed)
    {
        if (ShouldRefresh(changed, _watchedDirectories))
            ScheduleRefresh();
    }

    private void OnNetworkDirectoriesChanged(string drive, IReadOnlyCollection<string>? changedDirectories)
    {
        var changed = changedDirectories == null
            ? new[] { NormalizeDriveLetter(drive) }
            : changedDirectories.ToArray();
        if (ShouldRefresh(changed, _watchedDirectories))
            ScheduleRefresh();
    }

    private void ScheduleRefresh()
    {
        if (_disposed)
            return;

        _ = _dispatcher.BeginInvoke(new Action(() =>
        {
            if (!_disposed && !_timer.IsEnabled)
                _timer.Start();
        }), DispatcherPriority.Background);
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        if (_disposed)
            return;

        _timer.Stop();
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
        if (_disposed)
            return;

        _disposed = true;
        UserNetworkDriveSearch.DirectoriesChanged -= OnNetworkDirectoriesChanged;
        _subscription.Cancel();
        _subscription.Dispose();
        _timer.Stop();
        _timer.Tick -= OnTimerTick;
    }
}
