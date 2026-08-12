using System.Windows.Threading;
using Lertaro.Core.Services.Network;
using Lertaro.Core.Services.Plugin.DirectoryIndex;
using Lertaro.Core.Services.Search;

namespace Lertaro.App.Views.SpaceAnalyzer;

/// <summary>
/// Coalesces live index notifications into bounded UI refreshes. Split out to keep the window below
/// the repository's per-file line limit; it owns subscriptions only and leaves presentation to the window.
/// </summary>
internal sealed class SpaceAnalyzerRefreshWatcher : IDisposable
{
    private static readonly string[] LocalRoots = Enumerable.Range('A', 26).Select(letter => $"{(char)letter}:\\").ToArray();
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _timer;
    private readonly Func<Task> _reload;
    private CancellationTokenSource? _directoryCts;
    private volatile string[] _watchedPaths = Array.Empty<string>();
    private volatile bool _atRoot;
    private volatile bool _active;
    private bool _networkSubscribed;
    private bool _isReloading;
    private volatile bool _disposed;
    private int _pending;

    public SpaceAnalyzerRefreshWatcher(Dispatcher dispatcher, Func<Task> reload)
    {
        _dispatcher = dispatcher;
        _reload = reload;
        _timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(750)
        };
        _timer.Tick += OnTimerTick;
    }

    public void Watch(string? directory)
    {
        if (_disposed)
            return;
        var atRoot = string.IsNullOrEmpty(directory);
        var watched = atRoot ? LocalRoots : new[] { directory! };
        var unchanged = _atRoot == atRoot && _watchedPaths.SequenceEqual(watched, StringComparer.OrdinalIgnoreCase);
        if (!_networkSubscribed)
        {
            UserNetworkDriveSearch.DirectoriesChanged += OnNetworkDirectoriesChanged;
            _networkSubscribed = true;
        }
        if (_active && unchanged)
            return;

        _active = true;
        _atRoot = atRoot;
        _watchedPaths = watched;
        _directoryCts?.Cancel();
        _directoryCts?.Dispose();
        var subscription = new CancellationTokenSource();
        _directoryCts = subscription;
        _ = Task.Run(() => WatchLocalDirectoriesAsync(watched, subscription.Token));
    }

    private async Task WatchLocalDirectoriesAsync(IReadOnlyList<string> watched, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await DirectoryChangeStream.SubscribeAsync(watched, _ => ScheduleRefresh(), token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                Core.Logger.Log($"[SpaceAnalyzer] Index change subscription dropped, retrying: {ex.Message}", Core.LogLevel.Debug);
            }

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

    private void OnNetworkDirectoriesChanged(string drive, IReadOnlyCollection<string>? changedDirectories)
    {
        if (_atRoot ||
            (changedDirectories == null && _watchedPaths.Any(path => WatchedDirectoryMatcher.Touches(path, drive))) ||
            (changedDirectories != null && WatchedDirectoryMatcher.Match(_watchedPaths, changedDirectories).Count > 0))
            ScheduleRefresh();
    }

    private void ScheduleRefresh()
    {
        if (_disposed || !_active || Interlocked.Exchange(ref _pending, 1) != 0)
            return;

        _ = _dispatcher.BeginInvoke(new Action(() =>
        {
            if (!_disposed && _active && !_timer.IsEnabled)
                _timer.Start();
        }));
    }

    private async void OnTimerTick(object? sender, EventArgs e)
    {
        if (Interlocked.Exchange(ref _pending, 0) == 0)
        {
            if (!_isReloading)
                _timer.Stop();
            return;
        }
        if (_isReloading)
        {
            Interlocked.Exchange(ref _pending, 1);
            return;
        }

        _isReloading = true;
        try
        {
            await _reload();
        }
        finally
        {
            _isReloading = false;
            if (Volatile.Read(ref _pending) == 0)
                _timer.Stop();
        }
    }

    public void Dispose()
    {
        _disposed = true;
        Pause();
        _timer.Tick -= OnTimerTick;
    }

    public void Pause()
    {
        _active = false;
        Interlocked.Exchange(ref _pending, 0);
        _timer.Stop();
        if (_networkSubscribed)
        {
            UserNetworkDriveSearch.DirectoriesChanged -= OnNetworkDirectoriesChanged;
            _networkSubscribed = false;
        }
        _directoryCts?.Cancel();
        _directoryCts?.Dispose();
        _directoryCts = null;
    }
}
