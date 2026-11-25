namespace Lertaro.Core.DriveMonitoring;

internal sealed class FolderDriveMonitor : IDisposable
{
    private const int DebounceMilliseconds = 250;

    private readonly record struct PendingChange(WatcherChangeTypes ChangeType, string Path, string? OldPath);

    private readonly string _drive;
    // Returns whether the change could be applied only in part, so the drive owes a re-walk.
    private readonly Func<WatcherChangeTypes, string, string?, bool> _onChange;
    private readonly Action? _onReindexRequired;
    private readonly CancellationToken _token;
    private readonly object _gate = new();
    private readonly DriveWatcherHost _host;
    private readonly List<PendingChange> _pending = new();
    private readonly WatcherLossRefreshGate _lossGate = new();
    private Timer? _debounce;
    private volatile bool _disposed;

    public FolderDriveMonitor(string drive, Func<WatcherChangeTypes, string, string?, bool> onChange, CancellationToken token, Action? onReindexRequired = null)
    {
        _drive = drive;
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

        // A re-walk the batch owes is asked for once, after the whole batch: the request marks the drive
        // "indexing" at once, so every later change in it would be flagged as missed by that rebuild and buy
        // a second walk, although the walk has not started yet. Whether a rebuild is already pending or
        // running is SearchEngineDriveMaintenance's to decide, not this monitor's.
        var owesRewalk = false;
        // Last resort: this runs on a Timer callback, where an unhandled exception terminates the process,
        // and _onChange reaches the index, its subscribers and the persist debouncer. Same answer as
        // WatcherManager's handlers on the network side: log it and re-walk the drive, which also covers
        // the rest of this batch.
        // ponytail: not gated like WatcherLossRefreshGate, so a change that fails every time asks for a
        // re-walk on every flush (QueueDriveRebuild drops duplicates while one is pending). Gate it per
        // drive if that ever shows up in the logs.
        try
        {
            foreach (var item in batch)
            {
                if (_token.IsCancellationRequested)
                    return;
                owesRewalk |= _onChange(item.ChangeType, item.Path, item.OldPath);
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"[{nameof(FolderDriveMonitor)}] Applying a change on {_drive} failed, re-walking the drive: {ex}", LogLevel.Error);
            owesRewalk = true;
        }

        // Asked even if this monitor was disposed meanwhile: the debt belongs to the drive, and a change
        // already applied to its index is one the replacement monitor (EnsureMonitor after a rebuild disposes
        // this one without cancelling its token) never hears about again. For a drive removed meanwhile the
        // request is the same stray one this catch and the watcher's Error path could already make: the
        // rebuild fails on the unmounted volume and SettleFailedBuild parks it for re-insertion. A cancelled
        // token (shutdown) asks nothing.
        if (owesRewalk && !_token.IsCancellationRequested)
            _onReindexRequired?.Invoke();
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
