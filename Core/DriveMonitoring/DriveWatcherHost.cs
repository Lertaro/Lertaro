using Lertaro.Core.Indexer.NetworkDrive.Walk;
namespace Lertaro.Core.DriveMonitoring;

internal sealed class DriveWatcherHost : IDisposable
{
    private readonly string _name;
    private readonly string _drive;
    private readonly string _rootPath;
    private readonly Func<string, bool> _exists;
    private readonly Func<FileSystemWatcher, string, Action, Action, Action<string>, bool> _configure;
    private readonly Action<string> _onLog;
    private readonly TimeSpan _retryDelay;
    private readonly object _gate = new();
    private FileSystemWatcher? _watcher;
    private bool _retryInFlight;
    private bool _disposed;

    public DriveWatcherHost(string name, string drive, Func<string, bool> exists,
        Func<FileSystemWatcher, string, Action, Action, Action<string>, bool> configure, Action<string> onLog,
        TimeSpan? retryDelay = null)
    {
        _name = name;
        _drive = drive;
        // A bare drive letter ("C") needs ":\" appended. A UNC path just needs a trailing separator.
        // Anything else (a folder-index target, e.g. "Z:\Archive") is already a full path -- the old
        // UNC-or-append-colon check appended ":\" there too, producing "Z:\Archive:\", a path that can never
        // exist -- so Directory.Exists(_rootPath) always failed and the watcher silently never started.
        _rootPath = PathHelpers.BuildSourceRoot(drive);
        _exists = exists;
        _configure = configure;
        _onLog = onLog;
        _retryDelay = retryDelay ?? TimeSpan.FromSeconds(10);
    }

    public void Start() => EnsureWatcher();

    public void Dispose()
    {
        _disposed = true;
        lock (_gate)
        {
            _watcher?.Dispose();
            _watcher = null;
        }
    }

    private void EnsureWatcher()
    {
        lock (_gate)
        {
            if (_disposed || _watcher != null || !_exists(_rootPath))
                return;

            try
            {
                var watcher = new FileSystemWatcher(_rootPath);
                if (_configure(watcher, _drive, RestartWatcher, ScheduleRetry, LogError))
                {
                    watcher.EnableRaisingEvents = true;
                    _watcher = watcher;
                    _onLog($"[{_name}] Started monitoring {_drive}: via FileSystemWatcher.");
                }
                else
                {
                    watcher.Dispose();
                }
            }
            catch (Exception ex)
            {
                LogError($"Failed to start monitoring {_drive}: {ex.Message}");
                ScheduleRetry();
            }
        }
    }

    private void RestartWatcher()
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            _watcher?.Dispose();
            _watcher = null;
        }

        ScheduleRetry();
    }

    private void ScheduleRetry()
    {
        lock (_gate)
        {
            if (_retryInFlight || _disposed)
                return;
            _retryInFlight = true;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                while (!_disposed)
                {
                    await Task.Delay(_retryDelay).ConfigureAwait(false);
                    if (_disposed)
                        return;
                    EnsureWatcher();
                    lock (_gate)
                    {
                        if (_watcher != null)
                            return;
                    }
                }
            }
            finally
            {
                lock (_gate)
                    _retryInFlight = false;
            }
        });
    }

    private void LogError(string message) => _onLog($"[{_name}] {message}");
}
