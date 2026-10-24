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
    internal readonly HashSet<string> _pendingDriveRebuilds = new(StringComparer.OrdinalIgnoreCase);
    // One CancellationTokenSource per in-flight rebuild -- guarded by the same lock as
    // _pendingDriveRebuilds since a CTS should exist for exactly the lifetime a drive is in that set.
    // Lets CancelDriveRebuild (SearchEngineDriveMaintenanceCancellationExtensions.cs) stop a single
    // drive's own rebuild without touching the app-wide lifetime token (_token()) every other drive's
    // monitor also derives from. internal rather than private so that extension method can reach it.
    internal readonly Dictionary<string, CancellationTokenSource> _activeRebuildCts = new(StringComparer.OrdinalIgnoreCase);
    internal readonly HashSet<string> _rebuildAfterRemoval = new(StringComparer.OrdinalIgnoreCase);
    public bool HasPendingRebuilds { get { lock (_pendingDriveRebuilds) return _pendingDriveRebuilds.Count > 0; } }

    public SearchEngineDriveMaintenance(
        UsnIndexer indexer,
        Func<MachineSettings> settings,
        Func<CancellationToken> token,
        Func<bool> isRebuilding,
        Action onActivityCompleted)
    {
        _indexer = indexer;
        _settings = settings;
        _token = token;
        _isRebuilding = isRebuilding;
        _onActivityCompleted = onActivityCompleted;
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
    public bool RebuildDriveIndex(string drive)
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
                name => RebuildDriveIndex(name));
        if (!_settings().IsLocalDriveEnabled(driveId))
        {
            // The App discards a false reply without saying anything, so this is the only trace a rebuild
            // click that did nothing leaves -- and "the volume id changed" is the one reason a user cannot
            // fix by re-clicking.
            Logger.Log($"[SearchEngine] Ignored rebuild request for drive {drive}: its volume is not among the indexed drives.", LogLevel.Warn);
            return false;
        }
        return QueueDriveRebuild(drive, forceRebuild: true);
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
    public void QueueDriveRebuild(string drive) => QueueDriveRebuild(drive, forceRebuild: false);

    internal bool QueueDriveRebuild(string drive, bool forceRebuild)
    {
        lock (_pendingDriveRebuilds)
        {
            if (_isRebuilding())
            {
                // Dropped on purpose, not parked: the startup initializer owns its own deferred set for
                // the requests it makes, and every caller that matters (a status poll for a pending
                // drive, a re-insertion waiter) asks again. Parking here as well duplicated the whole
                // startup build -- one flush of five parked drives re-ran every $MFT scan after the
                // initial build had just finished.
                Logger.Log($"[SearchEngine] Ignored rebuild request for drive {drive}: a full rebuild is running.", LogLevel.Debug);
                return false;
            }
            if (!_pendingDriveRebuilds.Add(drive))
            {
                Logger.Log($"[SearchEngine] Ignored duplicate rebuild request for drive {drive}.");
                return false;
            }
        }
        UpdateMaintenanceBusyState();
        _indexer.SetDriveState(drive, "indexing", resetCounts: true);
        Task.Run(() => RebuildDrive(drive, forceRebuild));
        return true;
    }

    private void RebuildDrive(string drive, bool forceRebuild)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(_token());
        var retryAfterRemoval = false;
        lock (_pendingDriveRebuilds)
            _activeRebuildCts[drive] = cts;
        try
        {
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
            DriveArrivalRecovery.SettleFailedBuild(_indexer, drive, wasCancelled: true, _token(), QueueDriveRebuild);
        }
        catch (Exception ex)
        {
            Logger.Log($"[SearchEngine] Failed to build drive {drive}: {ex.Message}", LogLevel.Error);
            DriveArrivalRecovery.SettleFailedBuild(_indexer, drive, wasCancelled: false, _token(), QueueDriveRebuild);
        }
        finally
        {
            lock (_pendingDriveRebuilds)
            {
                _pendingDriveRebuilds.Remove(drive);
                if (_activeRebuildCts.Remove(drive, out var current) && ReferenceEquals(current, cts))
                    cts.Dispose();
                retryAfterRemoval = _rebuildAfterRemoval.Remove(drive);
            }
            UpdateMaintenanceBusyState();
            _onActivityCompleted();
        }
        if (retryAfterRemoval)
            QueueDriveRebuild(drive, forceRebuild: true);
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
            DriveArrivalRecovery.SettleFailedBuild(_indexer, drive, wasCancelled, _token(),
                name => RebuildDriveIndex(name));
            return;
        }

        EnsureDriveMonitor(drive, metadata[0].JournalId, metadata[0].NextUsn);
        // Same missed-folder-change check as DriveRecovery.RestoreOrRebuild -- queue a rebuild for it.
        if (_indexer.ConsumeMissedFolderChangeDuringRebuild(drive))
            QueueDriveRebuild(drive);
    }

    private void EnsureDriveMonitor(string drive, ulong journalId, long nextUsn) =>
        DriveMonitorFactory.EnsureMonitor(_indexer, drive, journalId, nextUsn, _token(), QueueDriveRebuild,
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
