namespace Lertaro.Core.DriveMonitoring;

internal sealed class FolderDriveMonitor : IDisposable
{
    private const int DebounceMilliseconds = 250;

    private readonly record struct PendingChange(WatcherChangeTypes ChangeType, string Path, string? OldPath);

    private readonly string _drive;
    private readonly Action<WatcherChangeTypes, string, string?> _onChange;
    private readonly CancellationToken _token;
    private readonly object _gate = new();
    private readonly DriveWatcherHost _host;
    private readonly List<PendingChange> _pending = new();
    private Timer? _debounce;
    private bool _disposed;

    public FolderDriveMonitor(string drive, Action<WatcherChangeTypes, string, string?> onChange, CancellationToken token)
    {
        _drive = drive;
        _onChange = onChange;
        _token = token;
        _host = new DriveWatcherHost(
            nameof(FolderDriveMonitor),
            drive,
            Directory.Exists,
            ConfigureWatcher,
            message => Logger.Log(message, LogLevel.Warn));
    }

    public void Start() => _host.Start();

    private bool ConfigureWatcher(FileSystemWatcher watcher, string drive, Action restart, Action retry, Action<string> logError)
    {
        watcher.IncludeSubdirectories = true;
        watcher.InternalBufferSize = 64 * 1024;
        watcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite |
                               NotifyFilters.Size | NotifyFilters.Attributes | NotifyFilters.CreationTime;
        FileSystemEventHandler changed = (_, e) => Schedule(e.ChangeType, e.FullPath, null);
        RenamedEventHandler renamed = (_, e) => Schedule(WatcherChangeTypes.Renamed, e.FullPath, e.OldFullPath);
        watcher.Created += changed;
        watcher.Changed += changed;
        watcher.Deleted += changed;
        watcher.Renamed += renamed;
        watcher.Error += (_, e) =>
        {
            var ex = e.GetException();
            logError($"Watcher error on {drive}: {ex?.Message ?? "unknown"}");
            restart();
        };
        return true;
    }

    private void Schedule(WatcherChangeTypes changeType, string path, string? oldPath = null)
    {
        if (_token.IsCancellationRequested)
            return;

        lock (_gate)
        {
            if (_disposed)
                return;
            _pending.Add(new PendingChange(changeType, path, oldPath));

            // One timer, re-armed, instead of disposing and rebuilding it per event: a raw
            // FileSystemWatcher event arrived on this path thousands of times a second during the large
            // copies this monitor exists for (it drives the FAT32/exFAT volumes with no USN journal), and
            // each one allocated a Timer and a finalizer registration only to throw it away 250 ms later.
            // The ceiling that remains -- _pending itself, which still grows with the number of distinct
            // events until the drive goes quiet -- is IDX-01: bounding it needs a per-directory re-scan the
            // delta applier has no entry point for yet.
            _debounce ??= new Timer(_ => FlushPending(), null, DebounceMilliseconds, Timeout.Infinite);
            _debounce.Change(DebounceMilliseconds, Timeout.Infinite);
        }
    }

    private void FlushPending()
    {
        PendingChange[] batch;
        lock (_gate)
        {
            if (_pending.Count == 0)
                return;

            batch = _pending.ToArray();
            _pending.Clear();
        }

        foreach (var item in batch)
        {
            if (_token.IsCancellationRequested)
                return;
            _onChange(item.ChangeType, item.Path, item.OldPath);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            _debounce?.Dispose();
            _debounce = null;
        }
        _host.Dispose();
    }
}
