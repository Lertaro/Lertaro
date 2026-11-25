using Lertaro.Core.Indexer.Usn;
using Lertaro.Core.IndexV2.Persistence;
namespace Lertaro.Core.DriveMonitoring;

internal sealed class SearchEngineDriveMaintenance
{
    private static readonly string IndexCacheDir = LocalDriveCacheLocator.DefaultCacheDir;
    private readonly UsnIndexer _indexer;
    private readonly Func<MachineSettings> _settings;
    private readonly Func<CancellationToken> _token;
    private readonly Func<bool> _isRebuilding;
    private readonly Action _onActivityCompleted;
    private readonly TimeProvider _time;
    private readonly Action<Action> _runWork;
    internal readonly HashSet<string> _pendingDriveRebuilds = new(StringComparer.OrdinalIgnoreCase);
    // One CancellationTokenSource per in-flight rebuild -- guarded by the same lock as
    // _pendingDriveRebuilds since a CTS should exist for exactly the lifetime a drive is in that set.
    // Lets CancelDriveRebuild (SearchEngineDriveMaintenanceCancellationExtensions.cs) stop a single
    // drive's own rebuild without touching the app-wide lifetime token (_token()) every other drive's
    // monitor also derives from. internal rather than private so that extension method can reach it.
    internal readonly Dictionary<string, CancellationTokenSource> _activeRebuildCts = new(StringComparer.OrdinalIgnoreCase);
    // Drives owed a forced rebuild once their running one ends. Two requests land here: a failed removal's
    // recovery, and a rebuild asked for while the drive was unplugged, which is replayed on its return.
    private readonly HashSet<string> _rebuildAfterRemoval = new(StringComparer.OrdinalIgnoreCase);
    // Drives asked to re-index while their own rebuild was still running -- see QueueDriveRebuild(string).
    // A cancellation of that rebuild does not discard them: see RebuildDrive.
    private readonly HashSet<string> _rebuildAgain = new(StringComparer.OrdinalIgnoreCase);
    // A drive keeps asking when it loses changes faster than it can be walked: a FAT32/exFAT card filled by
    // a copy or a camera, whose watchers overflow (the walk's own recording, or the live monitor right after
    // each hand-over), or a journal drive whose monitor keeps failing. Walking it again at once would only
    // collect the next loss, so each such walk waits instead. The first wait is 30 s, long enough for a
    // typical burst of writes to settle. Each wait after that doubles while the drive keeps asking -- during
    // a walk, or within the last wait after one ends -- so the walk rate drops geometrically. A drive that
    // stays quiet for that long starts over from an immediate walk. Every request still ends in a walk.
    // ponytail: the cap leaves a drive that never goes quiet with one whole-volume walk every 10 min,
    // indefinitely. Upgrade path: a per-directory re-scan of what the lost events touched (IDX-01), so the
    // recovery is never a whole-volume walk.
    private static readonly TimeSpan FirstRetryDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromMinutes(10);
    // The one delayed retry each drive is waiting on, and how long it waited. Guarded by the
    // _pendingDriveRebuilds lock. A drive is never both here and pending: any rebuild that starts
    // supersedes its wait (see TryBeginRebuild). A monitor's or recovery's request that arrives during the
    // wait collapses into it. The retry is cancelled by a Stop or a removal request (CancelDriveRebuild), and
    // by the drive being removed from configuration (RefreshDrivesInStatus). The lifetime token it is linked
    // to also cancels it (a full rebuild, or Dispose). If the drive was deselected without that refresh, the
    // retry finds it deselected when it fires and does nothing.
    private readonly Dictionary<string, (CancellationTokenSource Retry, TimeSpan Delay)> _delayedRebuilds = new(StringComparer.OrdinalIgnoreCase);
    // When each drive's last rebuild chain ended, as the end of the wait a request arriving before then still
    // gets (see TryBeginRebuild), and that wait. Guarded by the _pendingDriveRebuilds lock.
    private readonly Dictionary<string, (DateTimeOffset Until, TimeSpan Delay)> _quietUntil = new(StringComparer.OrdinalIgnoreCase);
    public bool HasPendingRebuilds { get { lock (_pendingDriveRebuilds) return _pendingDriveRebuilds.Count > 0; } }

    // False only for a mounted drive whose volume is not selected. An unmounted drive cannot name its volume,
    // so it counts as selected: its rebuild fails fast and parks it for re-insertion. Queries the volume, so
    // callers keep it out of the _pendingDriveRebuilds lock.
    private bool IsSelected(string drive) =>
        VolumeHelper.GetVolumeId(drive) is not { } volumeId || _settings().IsLocalDriveEnabled(volumeId);

    // `time` is the clock the delayed retries wait on. `runWork` runs a rebuild's work off the caller's thread.
    // SearchEngine passes TimeProvider.System and Task.Run.
    public SearchEngineDriveMaintenance(
        UsnIndexer indexer,
        Func<MachineSettings> settings,
        Func<CancellationToken> token,
        Func<bool> isRebuilding,
        Action onActivityCompleted,
        TimeProvider time,
        Action<Action> runWork)
    {
        _indexer = indexer;
        _settings = settings;
        _token = token;
        _isRebuilding = isRebuilding;
        _onActivityCompleted = onActivityCompleted;
        _time = time;
        _runWork = runWork;
    }
    public void RefreshDrivesInStatus()
    {
        try
        {
            var detected = VolumeHelper.DetectIndexableLocalDrives();
            var detectedSet = new HashSet<string>(detected, StringComparer.OrdinalIgnoreCase);
            var cachedEntries = LocalDriveCacheLocator.ListCachedDrives(IndexCacheDir);
            var cachedPaths = cachedEntries.ToDictionary(e => e.Drive, e => e.Path, StringComparer.OrdinalIgnoreCase);
            var visible = detected.Concat(cachedEntries.Select(e => e.Drive)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(d => d).ToList();
            var settings = _settings();
            var supported = detected.Where(d => settings.IsLocalDriveEnabled(VolumeHelper.GetVolumeId(d))).ToList();
            var enabled = new HashSet<string>(supported, StringComparer.OrdinalIgnoreCase);
            var disabled = visible.Where(d => !enabled.Contains(d)).ToList();
            var drivesToBuild = new List<string>();
            lock (_indexer.LockObj)
            {
                var current = _indexer.Status.Drives.ToDictionary(d => d.Drive, StringComparer.OrdinalIgnoreCase);
                var next = new List<UsnIndexer.DriveIndexStatus>();
                foreach (var drive in visible)
                    next.Add(DriveMaintenanceHelper.UpdateStatus(drive, detectedSet.Contains(drive), enabled.Contains(drive), IndexCacheDir, current, drivesToBuild, cachedPaths));
                _indexer.Status.Drives = next;
            }
            foreach (var drive in disabled)
            {
                _indexer.RemoveDriveMonitor(drive);
                _indexer.DropDriveFromRuntime(drive);
                // A mounted drive listed here was removed from configuration, so its waiting retry has nothing
                // left to recover. If it is selected again, UpdateStatus sees the disabled->enabled edge
                // (ShouldRestoreAfterEnable) and queues the restore itself. An unmounted drive keeps its retry:
                // that rebuild fails fast and parks the drive with a re-insertion waiter (SettleFailedBuild).
                if (detectedSet.Contains(drive))
                    CancelDelayedRebuild(drive);
            }
            // Not forced: QueueDriveRebuild restores from the cache and catches up through the USN
            // journal, and only falls back to a full $MFT scan when that cache cannot be used. Forcing
            // here turned every status poll that still saw "pending" into another whole-volume scan.
            foreach (var drive in drivesToBuild)
                QueueDriveRebuild(drive, forceRebuild: false);
        }
        catch (Exception ex)
        {
            Logger.Log($"[SearchEngine] Failed to refresh drive statuses: {ex.Message}", LogLevel.Error);
        }
    }
    public UsnIndexer.IndexerStatus BuildStatusSnapshot()
    {
        RefreshDrivesInStatus();
        // Under LockObj like every other Status mutation (UpdateMaintenanceBusyState, PopulateCountsFromCache,
        // the initializer): SnapshotStatus() deep-copies under that same lock, and an unlocked write here let a
        // concurrent snapshot observe a half-updated status.
        lock (_indexer.LockObj)
            _indexer.Status.IsMaintenanceBusy = _isRebuilding() || HasPendingRebuilds;
        PopulateCountsFromCache();
        // Return a locked, deep-copied snapshot (UsnIndexer.SnapshotStatus), not the live, mutable Status
        // object. PipeResponseBinarySerializer.WriteStatusAsync reads every drive's Files/Dirs/State
        // TWICE (once to size the buffer, once to write it) with no lock held; a concurrent
        // UpdateDriveProgress call from a scanner thread (running under LockObj) mutating the same
        // objects between those two passes produced a torn, internally-inconsistent snapshot -- visible
        // in the UI as the item count flickering up and down during a rebuild instead of only increasing.
        return _indexer.SnapshotStatus();
    }
    public bool RebuildDriveIndex(string drive) => RebuildDriveIndex(drive, followUpIfBusy: false);

    // `followUpIfBusy` (see QueueDriveRebuild): false for a client's request, whose false goes back to the
    // client. True for a re-insertion waiter replaying the request. Nobody would retry that request if it were
    // refused, and the restore that the volume's arrival may already have started is not the forced rebuild
    // that was asked for.
    internal bool RebuildDriveIndex(string drive, bool followUpIfBusy)
    {
        drive = DriveMaintenanceHelper.NormalizeDrive(drive);
        if (drive.Length == 0)
            return false;
        var driveId = VolumeHelper.GetVolumeId(drive);
        if (driveId == null)
            // An unmounted volume cannot answer for its own volume id (GetVolumeInformationW needs the
            // mount), so the enabled check below is impossible here. Refusing on that floor is exactly what
            // made a rebuild clicked while the disk was unplugged never happen after it went back in: the
            // request was recorded nowhere. Take it, park the stale cache, and rebuild on re-insertion.
            return DriveArrivalRecovery.ArmRebuildForReinsertion(_indexer, IndexCacheDir, drive, _token(),
                name => RebuildDriveIndex(name, followUpIfBusy: true));
        if (!_settings().IsLocalDriveEnabled(driveId))
        {
            // The App discards a false reply without saying anything, so this is the only trace a rebuild
            // click that did nothing leaves -- and "the volume id changed" is the one reason a user cannot
            // fix by re-clicking.
            Logger.Log($"[SearchEngine] Ignored rebuild request for drive {drive}: its volume is not among the indexed drives.", LogLevel.Warn);
            return false;
        }
        return QueueDriveRebuild(drive, forceRebuild: true, followUpIfBusy: followUpIfBusy);
    }
    public bool DeleteDriveIndex(string drive)
    {
        drive = DriveMaintenanceHelper.NormalizeDrive(drive);
        if (drive.Length == 0)
            return false;
        LocalDriveCacheLocator.Delete(IndexCacheDir, drive);
        _indexer.DropDriveFromRuntime(drive);
        var detected = VolumeHelper.DetectIndexableLocalDrives();
        var detectedSet = new HashSet<string>(detected, StringComparer.OrdinalIgnoreCase);
        var isPresent = detectedSet.Contains(drive);
        var isEnabled = isPresent && _settings().IsLocalDriveEnabled(VolumeHelper.GetVolumeId(drive));
        lock (_indexer.LockObj)
        {
            var status = _indexer.Status.Drives.FirstOrDefault(d => d.Drive.Equals(drive, StringComparison.OrdinalIgnoreCase));
            if (status != null)
            {
                status.Enabled = isEnabled;
                status.Kind = isPresent ? VolumeHelper.GetDisplayFileSystemType(drive) : "-";
                status.State = isPresent ? (isEnabled ? "ready" : "disabled") : "unavailable";
                status.Files = 0;
                status.Dirs = 0;
            }
        }
        Logger.Log($"[SearchEngine] Deleted cached index for drive {drive} by client request.");
        _indexer.RaiseDirectoriesChanged(drive, null);
        return true;
    }
    // The re-index callback every monitor and recovery path is handed. A request for a drive whose own
    // rebuild is still running -- the missed-folder-change follow-up that rebuild raises at its end, or its
    // old monitor giving up mid-walk -- used to be refused below as a duplicate and lost. It now runs after
    // that rebuild instead (see RebuildDrive), like QueueDriveRebuildAfterRemoval: at once the first time, and
    // after a growing delay when the drive keeps asking during each follow-up.
    public void QueueDriveRebuild(string drive) => QueueDriveRebuild(drive, forceRebuild: false, followUpIfBusy: true);

    // `followUpIfBusy`: a request for a drive whose rebuild is already running is recorded to run after it, as
    // a forced follow-up (_rebuildAfterRemoval) or a plain one (_rebuildAgain). False refuses the request
    // instead, for the two callers that do not need a follow-up: a manual rebuild, whose false goes back to
    // the client, and a status poll asking to build a "pending" drive, which the running rebuild is already
    // building.
    internal bool QueueDriveRebuild(string drive, bool forceRebuild, bool followUpIfBusy = false)
    {
        CancellationTokenSource? cts;
        lock (_pendingDriveRebuilds)
            cts = TryBeginRebuild(drive, forceRebuild, followUpIfBusy);
        if (cts == null)
            return false;
        StartRebuild(drive, forceRebuild, cts, followUpDelay: null);
        return true;
    }

    // The whole decision (refuse, record a follow-up, collapse into a waiting retry, or take the drive) runs
    // inside the critical section the caller holds on _pendingDriveRebuilds. When the "is it pending" check
    // and the Add ran under two separate acquisitions, a request that lost the race between them fell into
    // the duplicate branch and was neither run nor recorded. Only StartRebuild, which runs outside the lock,
    // publishes state and starts the work.
    private CancellationTokenSource? TryBeginRebuild(string drive, bool forceRebuild, bool followUpIfBusy)
    {
        if (_isRebuilding())
        {
            // Dropped on purpose, not parked: the startup initializer owns its own deferred set for
            // the requests it makes, and every caller that matters (a status poll for a pending
            // drive, a re-insertion waiter) asks again. Parking here as well duplicated the whole
            // startup build -- one flush of five parked drives re-ran every $MFT scan after the
            // initial build had just finished.
            Logger.Log($"[SearchEngine] Ignored rebuild request for drive {drive}: a full rebuild is running.", LogLevel.Debug);
            return null;
        }
        if (_pendingDriveRebuilds.Contains(drive))
        {
            if (!followUpIfBusy)
                Logger.Log($"[SearchEngine] Ignored duplicate rebuild request for drive {drive}.");
            else if (forceRebuild)
                _rebuildAfterRemoval.Add(drive);
            else
                _rebuildAgain.Add(drive);
            return null;
        }
        // A monitor's or recovery's plain request while a retry waits joins it: the walk that retry runs
        // starts after this request, so it observes whatever this request was about. A forced one (a manual
        // rebuild, a removal that failed) asks for more than that walk, so it runs now and the retry it
        // supersedes is cancelled. So does a status refresh's restore, which comes for a drive just
        // re-selected (whose index the deselection dropped) or still "pending". That drive needs its walk now.
        if (!forceRebuild && followUpIfBusy && _delayedRebuilds.TryGetValue(drive, out var waiting) && !waiting.Retry.IsCancellationRequested)
            return null;
        // A monitor's or recovery's plain request soon after the drive's last rebuild ended (within that
        // rebuild's wait) is the drive asking again, as if it had asked during the walk: it waits, and the wait
        // keeps doubling. Without this, a FAT32/exFAT drive whose live monitor overflows right after every
        // hand-over -- the walk's own recording absorbs what happens during the walk -- restarted an immediate
        // whole-drive walk every time.
        if (_quietUntil.Remove(drive, out var quiet) && !forceRebuild && followUpIfBusy && _time.GetUtcNow() < quiet.Until)
        {
            ScheduleDelayedRebuild(drive, quiet.Delay);
            return null;
        }
        CancelDelayedRebuild(drive);
        _pendingDriveRebuilds.Add(drive);
        var cts = CancellationTokenSource.CreateLinkedTokenSource(_token());
        _activeRebuildCts[drive] = cts;
        return cts;
    }

    // Removes and cancels the drive's waiting retry; false when there is none. Cancel only: the retry's own
    // continuation (RunDelayedRebuild) disposes it, after it finds the entry gone.
    internal bool CancelDelayedRebuild(string drive)
    {
        lock (_pendingDriveRebuilds)
        {
            if (!_delayedRebuilds.Remove(drive, out var waiting))
                return false;
            waiting.Retry.Cancel();
            return true;
        }
    }

    // The caller holds _pendingDriveRebuilds, and the drive is neither pending nor waiting.
    private void ScheduleDelayedRebuild(string drive, TimeSpan delay)
    {
        Logger.Log($"[SearchEngine] Drive {drive} asked to re-index again soon after its last rebuild; retrying in {delay.TotalSeconds:0} s.", LogLevel.Warn);
        var retry = CancellationTokenSource.CreateLinkedTokenSource(_token());
        _delayedRebuilds[drive] = (retry, delay);
        // Not ExecuteSynchronously, so a Cancel() made under the lock never runs this continuation inline.
        _ = Task.Delay(delay, _time, retry.Token).ContinueWith(_ => RunDelayedRebuild(drive, retry), TaskScheduler.Default);
    }

    // The waiting retry's continuation: it runs when the delay elapses or when the retry is cancelled.
    private void RunDelayedRebuild(string drive, CancellationTokenSource retry)
    {
        CancellationTokenSource? cts = null;
        var delay = TimeSpan.Zero;
        // Deselected after the retry was scheduled, and no status refresh has cancelled it yet: re-selecting
        // the drive restores it (ShouldRestoreAfterEnable), so there is nothing left for this retry to do.
        var selected = !retry.IsCancellationRequested && IsSelected(drive);
        lock (_pendingDriveRebuilds)
        {
            // Not ours any more: whoever removed the entry also cancelled it. That is a Stop, a removal
            // request, the drive being deselected, or a rebuild that has started and supersedes the retry.
            if (_delayedRebuilds.TryGetValue(drive, out var waiting) && ReferenceEquals(waiting.Retry, retry))
            {
                _delayedRebuilds.Remove(drive);
                delay = waiting.Delay;
                // If the retry is cancelled while still ours, the lifetime token did it: either a full rebuild
                // covers the drive, or the engine is shutting down.
                if (selected)
                    cts = TryBeginRebuild(drive, forceRebuild: false, followUpIfBusy: true);
            }
        }
        retry.Dispose();
        if (cts != null)
            StartRebuild(drive, forceRebuild: false, cts, TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, MaxRetryDelay.Ticks)));
    }

    // `followUpDelay`: how long a follow-up requested during this rebuild waits. Null runs it at once, which
    // is what a rebuild started by an outside request gets.
    private void StartRebuild(string drive, bool forceRebuild, CancellationTokenSource cts, TimeSpan? followUpDelay)
    {
        UpdateMaintenanceBusyState();
        _indexer.SetDriveState(drive, "indexing", resetCounts: true);
        _runWork(() => RebuildDrive(drive, forceRebuild, cts, followUpDelay));
    }

    private void RebuildDrive(string drive, bool forceRebuild, CancellationTokenSource cts, TimeSpan? followUpDelay)
    {
        CancellationTokenSource? next = null;
        var nextForced = false;
        TimeSpan? nextFollowUpDelay = null;
        try
        {
            cts.Token.ThrowIfCancellationRequested();
            if (forceRebuild)
                ForceRebuildDrive(drive, cts.Token);
            else
                DriveRecovery.RestoreOrRebuild(_indexer, IndexCacheDir, drive, _token(), QueueDriveRebuild, cts.Token,
                    driveName => this.CancelDriveRebuild(driveName), driveName => this.QueueDriveRebuildAfterRemoval(driveName));
        }
        // ObjectDisposedException joins the cancellation branch for the same reason it does in
        // IndexBuilder.BuildDrives: cancelling disposes the volume handle, a SafeHandle cannot abort the
        // native read already in flight, and the next P/Invoke on the disposed handle throws this instead
        // of returning false. Left in the general branch a plain Stop request would report "failed".
        catch (Exception ex) when (ex is OperationCanceledException
                                   || (ex is ObjectDisposedException && cts.Token.IsCancellationRequested))
        {
            DriveArrivalRecovery.SettleFailedBuild(_indexer, drive, wasCancelled: true, cts.Token, _token(), QueueDriveRebuild, QueueDriveRebuild);
        }
        catch (Exception ex)
        {
            Logger.Log($"[SearchEngine] Failed to build drive {drive}: {ex.Message}", LogLevel.Error);
            DriveArrivalRecovery.SettleFailedBuild(_indexer, drive, wasCancelled: false, cts.Token, _token(), QueueDriveRebuild, QueueDriveRebuild);
        }
        finally
        {
            // Read before the lock, since it queries the volume. A drive deselected while it rebuilt gets no
            // follow-up: the status refresh has dropped (or is about to drop) its index and monitor, and
            // re-selecting it restores it through ShouldRestoreAfterEnable.
            var selected = IsSelected(drive);
            lock (_pendingDriveRebuilds)
            {
                // A plain request is a recovery obligation (a monitor's loss, a lost recording, a failed walk's
                // owed re-walk), and cancelling this walk does not discharge it: only a walk that starts after
                // the loss does. So a cancelled rebuild keeps it too, as a waiting retry rather than an
                // immediate walk: a Stop means "not now", and 30 s or more is not now. A removal's or a full
                // rebuild's cancellation leaves it to the walk those start; if theirs comes first, the retry
                // finds the drive rebuilding, unselected or gone, and folds in or fails fast.
                var rebuildAgain = _rebuildAgain.Remove(drive);
                var cancelled = cts.IsCancellationRequested;
                _pendingDriveRebuilds.Remove(drive);
                if (_activeRebuildCts.Remove(drive, out var current) && ReferenceEquals(current, cts))
                    cts.Dispose();
                // The next rebuild is taken here, under the same lock that just released this one, so no other
                // request can slip in between and leave it refused as a duplicate. TryBeginRebuild returns
                // null only when a full rebuild is running, and that rebuild covers the drive.
                var forcedAgain = _rebuildAfterRemoval.Remove(drive);
                if (!selected)
                {
                    if (forcedAgain || rebuildAgain)
                        Logger.Log($"[SearchEngine] Skipped the follow-up rebuild of drive {drive}: it is no longer among the indexed drives.");
                }
                else if (forcedAgain)
                {
                    // A forced scan also answers the plain follow-up, so rebuildAgain needs no run of its own.
                    nextForced = true;
                    next = TryBeginRebuild(drive, forceRebuild: true, followUpIfBusy: true);
                }
                else if (rebuildAgain && followUpDelay is null && !cancelled)
                {
                    next = TryBeginRebuild(drive, forceRebuild: false, followUpIfBusy: true);
                    nextFollowUpDelay = FirstRetryDelay;
                }
                // A follow-up asked for again, or asked for during a cancelled rebuild. Running it at once is how
                // a busy FAT32/exFAT drive loops forever, so it waits instead (see FirstRetryDelay).
                else if (rebuildAgain)
                    ScheduleDelayedRebuild(drive, followUpDelay ?? FirstRetryDelay);
                // Not asked again: the chain ends here, but a request within its wait still continues it.
                else
                {
                    var wait = followUpDelay ?? FirstRetryDelay;
                    _quietUntil[drive] = (_time.GetUtcNow() + wait, wait);
                }
            }
            UpdateMaintenanceBusyState();
            _onActivityCompleted();
        }
        if (next != null)
            StartRebuild(drive, nextForced, next, nextFollowUpDelay);
    }

    private void ForceRebuildDrive(string drive, CancellationToken token)
    {
        Logger.Log($"[SearchEngine] Rebuilding drive {drive} by client request.");
        lock (_indexer.LockObj)
        {
            _indexer.Status.State = "indexing";
            _indexer.Status.Progress = 0;
            _indexer.Status.ActiveDrives = new List<string> { drive };
        }
        _indexer.SetDriveState(drive, "indexing");
        // Watcher stopped, device notification kept -- same reasoning as DriveRecovery.RestoreOrRebuild.
        if (VolumeHelper.SupportsUsnJournal(drive))
            _indexer.ReleaseDriveMonitor(drive);
        var wasCancelled = false;
        var metadata = _indexer.BuildDrives(new[] { drive }, clearExisting: false, cacheDir: IndexCacheDir,
            getToken: _ => token, onDriveCancelled: _ => wasCancelled = true, forceFullScan: true);
        if (metadata.Count == 0)
        {
            // "cached" for a Stop, "failed" for an error, and -- if the volume went away mid-scan -- a
            // forced rebuild queued for the moment it is back, since this is the request the user made
            // against this one drive. See DriveRecovery.RestoreOrRebuild for the same three-way settle.
            DriveArrivalRecovery.SettleFailedBuild(_indexer, drive, wasCancelled, token, _token(),
                name => RebuildDriveIndex(name, followUpIfBusy: true), QueueDriveRebuild);
            return;
        }

        // Always handed over once the new index is in: see DriveRecovery.RestoreOrRebuild.
        EnsureDriveMonitor(drive, metadata[0].JournalId, metadata[0].NextUsn, token);
        // Same missed-folder-change check as DriveRecovery.RestoreOrRebuild -- queue a rebuild for it.
        if (_indexer.ConsumeMissedFolderChangeDuringRebuild(drive))
            QueueDriveRebuild(drive);
    }

    private void EnsureDriveMonitor(string drive, ulong journalId, long nextUsn, CancellationToken buildToken) =>
        DriveMonitorFactory.EnsureMonitor(_indexer, drive, journalId, nextUsn, _token(), buildToken, QueueDriveRebuild,
            driveName => this.CancelDriveRebuild(driveName), driveName => this.QueueDriveRebuildAfterRemoval(driveName));

    private void PopulateCountsFromCache()
    {
        var totalFiles = 0;
        var totalDirs = 0;

        lock (_indexer.LockObj)
        {
            foreach (var drive in _indexer.Status.Drives)
            {
                if (!drive.Enabled)
                {
                    drive.Files = 0;
                    drive.Dirs = 0;
                    continue;
                }
                if (drive.State == "indexing")
                {
                    totalFiles += drive.Files;
                    totalDirs += drive.Dirs;
                    continue;
                }

                if (_indexer._recordIndexes.TryGetValue(drive.Drive, out var live))
                {
                    var (files, dirs) = live.GetCounts();
                    drive.Files = files;
                    drive.Dirs = dirs;
                    totalFiles += files;
                    totalDirs += dirs;
                    continue;
                }

                if (string.IsNullOrWhiteSpace(drive.CachePath) || !File.Exists(drive.CachePath))
                {
                    if (drive.State != "indexing")
                    {
                        drive.Files = 0;
                        drive.Dirs = 0;
                    }
                    continue;
                }

                SnapshotFormat.Meta? meta;
                try
                {
                    meta = SnapshotFormat.TryReadHeaderFromFile(drive.CachePath);
                }
                catch (IOException)
                {
                    // Busy right now (e.g. a checkpoint mid-write) -- not evidence of corruption, try
                    // again next refresh instead of reporting stale/zeroed counts.
                    continue;
                }
                if (meta == null)
                    continue;

                if (drive.State != "indexing")
                {
                    drive.Files = meta.TotalFiles;
                    drive.Dirs = meta.TotalDirs;
                }
                totalFiles += meta.TotalFiles;
                totalDirs += meta.TotalDirs;
            }

            _indexer.Status.TotalFiles = totalFiles;
            _indexer.Status.TotalDirs = totalDirs;
        }
    }

    private void UpdateMaintenanceBusyState()
    {
        lock (_indexer.LockObj)
            _indexer.Status.IsMaintenanceBusy = _isRebuilding() || HasPendingRebuilds;
        _indexer.NotifyStatusChanged();
    }
}
