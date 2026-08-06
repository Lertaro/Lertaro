using Lertaro.Core.Indexer.NetworkDrive.Walk;
namespace Lertaro.Core.Indexer.NetworkDrive.Scheduling;

internal sealed class Scheduler : IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<string, CancellationTokenSource> _debounceCts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, CancellationTokenSource> _periodicCts = new(StringComparer.OrdinalIgnoreCase);
    // Present for exactly the drives currently queued-to-run or actively running -- this doubles as the
    // "is this drive busy" check (replacing what used to be a separate _refreshingDrives HashSet), since
    // a drive's entry here and its busy-ness are definitionally the same thing.
    private readonly Dictionary<string, CancellationTokenSource> _activeCts = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _pendingRefreshDrives = new(StringComparer.OrdinalIgnoreCase);
    // Cancelled only on Dispose() -- unlike the old shared _refreshCts (recreated on every StartRefresh
    // call), this never interrupts a drive that's still configured. Only a drive genuinely removed from
    // config gets its own _activeCts entry cancelled (see StartRefresh); everything else keeps running
    // undisturbed across an unrelated Configure() call (e.g. a settings Apply that only touched a
    // different drive, or an exclusions change elsewhere).
    private readonly CancellationTokenSource _lifetimeCts = new();

    private readonly Action<string, string> _onWatcherEnsure;
    private readonly Action<string> _onWatcherRemove;
    private readonly Action<string, string, int?, string?> _setStatus;
    private readonly Action<string, NetworkIndex> _onRefreshFinished;
    private readonly Action<string, FileRecordStore, NetworkDriveWalkStats, CancellationToken> _onPublishCheckpoint;
    private readonly Func<string, FileRecordStore?> _getPreviousStore;
    private readonly Action<string> _releaseCachedIndex;
    private readonly SchedulerQueueRunner _queueRunner;

    public Scheduler(Action<string, string> onWatcherEnsure, Action<string> onWatcherRemove, Action<string, string, int?, string?> setStatus,
        Action<string, NetworkIndex> onRefreshFinished, Action<string, FileRecordStore, NetworkDriveWalkStats, CancellationToken> onPublishCheckpoint,
        Func<string, FileRecordStore?> getPreviousStore, Action<string> releaseCachedIndex)
    {
        _onWatcherEnsure = onWatcherEnsure; _onWatcherRemove = onWatcherRemove; _setStatus = setStatus;
        _onRefreshFinished = onRefreshFinished; _onPublishCheckpoint = onPublishCheckpoint;
        _getPreviousStore = getPreviousStore; _releaseCachedIndex = releaseCachedIndex;
        _queueRunner = new SchedulerQueueRunner(_gate, _debounceCts, _activeCts, _pendingRefreshDrives, _lifetimeCts,
            setStatus, getPreviousStore, onPublishCheckpoint, onRefreshFinished, releaseCachedIndex);
    }

    public void QueueRefreshDrive(string drive, string reason) => _queueRunner.QueueRefreshDrive(drive, reason);

    public void StartRefresh(
        IReadOnlyList<string> drives,
        IReadOnlyDictionary<string, string> refreshModes,
        HashSet<string>? cachedDrives = null,
        IReadOnlyDictionary<string, DateTime>? lastUpdatedTimes = null)
    {
        lock (_gate)
        {
            // Every drive with any live bookkeeping (queued/running, debouncing, or on a periodic timer)
            // that is no longer in the incoming drives list is the only thing this call actually
            // interrupts -- a drive that's still configured never gets touched here, no matter why
            // StartRefresh was called.
            var removedDrives = _periodicCts.Keys
                .Concat(_debounceCts.Keys)
                .Concat(_activeCts.Keys)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Except(drives, StringComparer.OrdinalIgnoreCase)
                .ToList();
            foreach (var removed in removedDrives)
            {
                _onWatcherRemove(removed);
                RemovePeriodicLocked(removed);
                if (_debounceCts.Remove(removed, out var debounce))
                {
                    debounce.Cancel();
                    debounce.Dispose();
                }
                if (_activeCts.Remove(removed, out var active))
                {
                    active.Cancel();
                    active.Dispose();
                }
                _pendingRefreshDrives.Remove(removed);
            }

            foreach (var drive in drives)
            {
                var mode = refreshModes.TryGetValue(drive, out var value) ? value : "Manual";
                var lastUpdated = lastUpdatedTimes != null && lastUpdatedTimes.TryGetValue(drive, out var lu) ? lu : (DateTime?)null;
                _onWatcherEnsure(drive, mode);
                EnsurePeriodicRefreshLocked(drive, mode, lastUpdated);
            }
        }

        foreach (var drive in drives)
        {
            var mode = refreshModes.TryGetValue(drive, out var value) ? value : "Manual";
            bool alreadyActive;
            lock (_gate)
                alreadyActive = _activeCts.ContainsKey(drive);
            // A drive already queued/running (e.g. this call is only forcing a re-check of exclusions,
            // not a genuine config change to this specific drive) is left to finish its current pass --
            // it naturally picks up whatever's current the next time it runs, rather than being
            // interrupted and restarted from its last checkpoint for no real reason.
            var needsInitialRefresh = !alreadyActive
                && (cachedDrives == null || !cachedDrives.Contains(drive) || mode == "startup");
            if (needsInitialRefresh)
            {
                QueueRefreshDrive(drive, "configure");
            }
        }
    }

    private void EnsurePeriodicRefreshLocked(string drive, string mode, DateTime? lastUpdated)
    {
        var interval = IndexerHelper.GetRefreshInterval(mode);
        if (interval == null)
        {
            RemovePeriodicLocked(drive);
            return;
        }

        if (_periodicCts.ContainsKey(drive))
            return;

        var cts = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token);
        _periodicCts[drive] = cts;
        _ = Task.Run(async () =>
        {
            var firstRun = true;
            while (!cts.Token.IsCancellationRequested)
            {
                try
                {
                    var delay = interval.Value;
                    if (firstRun && lastUpdated.HasValue)
                    {
                        var timeSinceLastUpdate = DateTime.Now - lastUpdated.Value;
                        if (timeSinceLastUpdate > TimeSpan.Zero)
                        {
                            var remaining = interval.Value - timeSinceLastUpdate;
                            // If overdue or remaining time is less than 5s, delay for 5s to avoid startup bottleneck
                            delay = remaining > TimeSpan.FromSeconds(5) ? remaining : TimeSpan.FromSeconds(5);
                        }
                    }
                    firstRun = false;

                    await Task.Delay(delay, cts.Token).ConfigureAwait(false);
                    QueueRefreshDrive(drive, mode);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }, CancellationToken.None);
    }

    private void RemovePeriodicLocked(string drive)
    {
        if (_periodicCts.Remove(drive, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
        }
    }

    // User-initiated interrupt of a drive that's currently queued or actively refreshing. Removal from
    // _debounceCts/_activeCts still happens in the normal completion paths (QueueRefreshDrive's finally,
    // RefreshDriveLoop's finally), but reverting the status can't wait for those: a drive cancelled during
    // its debounce wait (not yet in _activeCts, so RefreshDrive's own cancellation catch never runs) would
    // otherwise stay stuck on "indexing" forever. Reverting it here too, unconditionally, is safe only
    // because this method is exclusively the user-facing Stop path -- a drive removed from config instead
    // goes through StartRefresh's own cleanup, which never calls this.
    public void CancelDrive(string drive)
    {
        lock (_gate)
        {
            if (_debounceCts.TryGetValue(drive, out var debounce))
                debounce.Cancel();
            if (_activeCts.TryGetValue(drive, out var active))
                active.Cancel();
        }
        _setStatus(drive, "cached", null, null);
    }

    public void Dispose()
    {
        _lifetimeCts.Cancel();

        lock (_gate)
        {
            foreach (var cts in _periodicCts.Values)
            {
                cts.Cancel();
                cts.Dispose();
            }
            _periodicCts.Clear();

            foreach (var cts in _debounceCts.Values)
            {
                cts.Cancel();
                cts.Dispose();
            }
            _debounceCts.Clear();

            foreach (var cts in _activeCts.Values)
            {
                cts.Cancel();
                cts.Dispose();
            }
            _activeCts.Clear();
        }

        _lifetimeCts.Dispose();
    }
}
