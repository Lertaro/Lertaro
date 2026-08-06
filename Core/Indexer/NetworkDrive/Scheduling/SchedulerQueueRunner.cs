using Lertaro.Core.Indexer.NetworkDrive.Walk;

namespace Lertaro.Core.Indexer.NetworkDrive.Scheduling;

// Debounce-and-run queueing for refreshes -- extracted out of Scheduler (composition, not a partial
// class) to keep that type's files under the project's line limit. QueueRefreshDrive debounces bursts
// of requests for the same drive (e.g. several watcher events in a row); StartRefreshDriveIfIdle/
// RefreshDriveLoop make sure only one refresh runs per drive at a time, re-running immediately if
// changes arrived while it was busy. Shares Scheduler's own _gate/_debounceCts/_activeCts/
// _pendingRefreshDrives/_lifetimeCts by reference rather than owning copies, since both types need to
// observe and mutate the same live bookkeeping under the same lock.
internal sealed class SchedulerQueueRunner
{
    private readonly object _gate;
    private readonly Dictionary<string, CancellationTokenSource> _debounceCts;
    private readonly Dictionary<string, CancellationTokenSource> _activeCts;
    private readonly HashSet<string> _pendingRefreshDrives;
    private readonly CancellationTokenSource _lifetimeCts;
    private readonly Action<string, string, int?, string?> _setStatus;
    private readonly Func<string, FileRecordStore?> _getPreviousStore;
    private readonly Action<string, FileRecordStore, NetworkDriveWalkStats, CancellationToken> _onPublishCheckpoint;
    private readonly Action<string, NetworkIndex> _onRefreshFinished;
    private readonly Action<string> _releaseCachedIndex;

    public SchedulerQueueRunner(
        object gate,
        Dictionary<string, CancellationTokenSource> debounceCts,
        Dictionary<string, CancellationTokenSource> activeCts,
        HashSet<string> pendingRefreshDrives,
        CancellationTokenSource lifetimeCts,
        Action<string, string, int?, string?> setStatus,
        Func<string, FileRecordStore?> getPreviousStore,
        Action<string, FileRecordStore, NetworkDriveWalkStats, CancellationToken> onPublishCheckpoint,
        Action<string, NetworkIndex> onRefreshFinished,
        Action<string> releaseCachedIndex)
    {
        _gate = gate;
        _debounceCts = debounceCts;
        _activeCts = activeCts;
        _pendingRefreshDrives = pendingRefreshDrives;
        _lifetimeCts = lifetimeCts;
        _setStatus = setStatus;
        _getPreviousStore = getPreviousStore;
        _onPublishCheckpoint = onPublishCheckpoint;
        _onRefreshFinished = onRefreshFinished;
        _releaseCachedIndex = releaseCachedIndex;
    }

    public void QueueRefreshDrive(string drive, string reason)
    {
        CancellationTokenSource? oldDebounce = null;
        CancellationTokenSource debounce;
        lock (_gate)
        {
            if (_lifetimeCts.IsCancellationRequested)
                return;

            if (_debounceCts.TryGetValue(drive, out oldDebounce))
                oldDebounce.Cancel();

            debounce = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token);
            _debounceCts[drive] = debounce;
        }

        try
        {
            oldDebounce?.Dispose();
        }
        catch { }

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(reason == "configure" ? TimeSpan.Zero : TimeSpan.FromSeconds(2), debounce.Token).ConfigureAwait(false);
                StartRefreshDriveIfIdle(drive, reason);
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
                // Swallow exception caused by CancellationTokenSource being disposed to prevent UnobservedTaskException crash
            }
            finally
            {
                lock (_gate)
                {
                    if (_debounceCts.TryGetValue(drive, out var current) && ReferenceEquals(current, debounce))
                        _debounceCts.Remove(drive);
                }

                try
                {
                    debounce.Dispose();
                }
                catch { }
            }
        }, CancellationToken.None);
    }

    private void StartRefreshDriveIfIdle(string drive, string reason)
    {
        CancellationTokenSource active;
        lock (_gate)
        {
            if (_lifetimeCts.IsCancellationRequested)
                return;

            if (_activeCts.ContainsKey(drive))
            {
                _pendingRefreshDrives.Add(drive);
                return;
            }

            // Linked to _lifetimeCts (not a per-Configure()-call token): this drive only stops early if
            // it's genuinely removed from config (StartRefresh cancels this specific entry) or the whole
            // Scheduler is disposed -- an unrelated Configure() call never touches it.
            active = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token);
            _activeCts[drive] = active;
        }

        _ = Task.Run(() => RefreshDriveLoop(drive, reason, active), active.Token);
    }

    private void RefreshDriveLoop(string drive, string reason, CancellationTokenSource active)
    {
        var token = active.Token;
        try
        {
            while (!token.IsCancellationRequested)
            {
                Logger.Log($"[NetworkIndexer] Refreshing {drive}: because {reason}");
                DriveRefreshRunner.RefreshDrive(drive, token, _setStatus, _getPreviousStore, _onPublishCheckpoint, _onRefreshFinished, _releaseCachedIndex);

                lock (_gate)
                {
                    if (!_pendingRefreshDrives.Remove(drive))
                        break;
                }

                reason = "pending changes";
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            bool stillPending;
            lock (_gate)
            {
                stillPending = _pendingRefreshDrives.Remove(drive);
                // Only clean up if this is still the entry for our own run -- a removed-then-quickly-
                // re-added drive may already have a newer entry (a fresh StartRefreshDriveIfIdle call) by
                // the time this finally block runs; removing/disposing that one would corrupt the busy
                // bookkeeping for the loop that's actually still using it.
                if (_activeCts.TryGetValue(drive, out var current) && ReferenceEquals(current, active))
                {
                    _activeCts.Remove(drive);
                    try { active.Dispose(); } catch { }
                }
            }

            // A request queued in the narrow window between this loop's own pending-check and reaching
            // here still deserves a run. But if the token is cancelled, this drive was removed from
            // config by StartRefresh -- re-queueing then would resurrect a drive just intentionally
            // stopped, so only re-queue on a normal (non-cancelled) exit.
            if (stillPending && !token.IsCancellationRequested)
                QueueRefreshDrive(drive, "pending changes");
        }
    }
}
