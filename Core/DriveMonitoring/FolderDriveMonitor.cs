namespace Lertaro.Core.DriveMonitoring;

internal sealed class FolderDriveMonitor : IDisposable
{
    private const int DebounceMilliseconds = 250;

    private readonly record struct PendingChange(WatcherChangeTypes ChangeType, string Path, string? OldPath);

    private readonly string _drive;
    private readonly Action<WatcherChangeTypes, string, string?> _onChange;
    private readonly Action? _onReindexRequired;
    private readonly CancellationToken _token;
    private readonly object _gate = new();
    private readonly DriveWatcherHost _host;
    private readonly List<PendingChange> _pending = new();
    // Paths already queued for an upsert since their last delete/rename, so a burst of Changed events on
    // one file (every write raises several) is applied once. Guarded by _gate, cleared with _pending.
    private readonly HashSet<string> _pendingUpserts = new(StringComparer.OrdinalIgnoreCase);
    // Our own data directory (the .idx caches and logs). A portable install on this very drive would
    // otherwise see each index rewrite as a change that schedules the next one, forever.
    private readonly string _ignoredPrefix;
    private readonly WatcherLossRefreshGate _lossGate = new();
    private Timer? _debounce;
    private bool _disposed;

    public FolderDriveMonitor(string drive, Action<WatcherChangeTypes, string, string?> onChange, CancellationToken token, Action? onReindexRequired = null)
    {
        _drive = drive;
        _ignoredPrefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Logger.SharedDataDir)) + Path.DirectorySeparatorChar;
        _onChange = onChange;
        _onReindexRequired = onReindexRequired;
        _token = token;
        _host = new DriveWatcherHost(
            nameof(FolderDriveMonitor),
            drive,
            Directory.Exists,
            ConfigureWatcher,
            Logger.Log,
            onStarted: () =>
            {
                if (!_disposed && !_token.IsCancellationRequested && _lossGate.Recovered(_drive))
                    _onReindexRequired?.Invoke();
            });
    }

    public void Start() => _host.Start();

    private bool ConfigureWatcher(FileSystemWatcher watcher, string drive, Action restart, Action retry, Action<string> logError)
    {
        watcher.IncludeSubdirectories = true;
        // An overflow costs a full re-walk of the drive (millions of entries on the large exFAT disks this
        // serves), so buy headroom: 64 KB is only the ceiling for network watchers, not local ones.
        watcher.InternalBufferSize = 256 * 1024;
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
            // restart() only brings live monitoring back: the changes this watcher failed to report (a
            // buffer overflow on a busy volume is the usual cause) are gone for good unless the drive is
            // re-walked, so ask for one. Requested once per outage -- see WatcherLossRefreshGate -- since
            // the retry path can raise several errors before the watcher is up again.
            if (RequestReindex())
                _onReindexRequired?.Invoke();
            restart();
        };
        return true;
    }

    // The callback is invoked outside the lock (it leads to a drive rebuild), but the decision itself is
    // taken under it so it observes the same _disposed/cancellation state Schedule does.
    private bool RequestReindex()
    {
        lock (_gate)
        {
            if (_disposed || _token.IsCancellationRequested)
                return false;

            return _lossGate.ShouldRequestRefresh(_drive);
        }
    }

    internal void Schedule(WatcherChangeTypes changeType, string path, string? oldPath = null)
    {
        if (_token.IsCancellationRequested || IsIgnored(path) && (oldPath == null || IsIgnored(oldPath)))
            return;

        lock (_gate)
        {
            if (_disposed)
                return;

            // Apply reads the path's CURRENT state at flush time, so a repeat upsert adds nothing -- unless a
            // delete or rename of that path sits between the two, which is why those drop it from the set.
            switch (changeType)
            {
                case WatcherChangeTypes.Changed when !_pendingUpserts.Add(path):
                    return;
                case WatcherChangeTypes.Created:
                    _pendingUpserts.Add(path);
                    break;
                case WatcherChangeTypes.Deleted:
                    _pendingUpserts.Remove(path);
                    break;
                case WatcherChangeTypes.Renamed:
                    if (oldPath != null) _pendingUpserts.Remove(oldPath);
                    _pendingUpserts.Add(path);
                    break;
            }
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

    private bool IsIgnored(string path) => path.StartsWith(_ignoredPrefix, StringComparison.OrdinalIgnoreCase);

    internal void FlushPending()
    {
        PendingChange[] batch;
        lock (_gate)
        {
            if (_pending.Count == 0)
                return;

            batch = _pending.ToArray();
            _pending.Clear();
            _pendingUpserts.Clear();
        }

        // Last resort: this runs on a Timer callback, where an unhandled exception terminates the process,
        // and _onChange reaches the index, its subscribers and the persist debouncer. Same answer as
        // WatcherManager's handlers on the network side: log it and re-walk the drive, which also covers
        // the rest of this batch.
        // ponytail: not gated like WatcherLossRefreshGate, so a change that fails every time asks for a
        // re-walk on every flush. SearchEngineDriveMaintenance folds the asks that arrive while a walk runs,
        // or while its retry waits, into one follow-up, and that follow-up's delay backs off to 10 min. Gate
        // it per drive if that ever shows up in the logs.
        try
        {
            foreach (var item in batch)
            {
                if (_token.IsCancellationRequested)
                    return;
                _onChange(item.ChangeType, item.Path, item.OldPath);
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"[{nameof(FolderDriveMonitor)}] Applying a change on {_drive} failed, re-walking the drive: {ex}", LogLevel.Error);
            if (!_disposed && !_token.IsCancellationRequested)
                _onReindexRequired?.Invoke();
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
