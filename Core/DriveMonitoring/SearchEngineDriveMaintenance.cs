using Lertaro.Core.Indexer.Usn;

using Lertaro.Core.IndexV2.Persistence;
namespace Lertaro.Core.DriveMonitoring;

internal sealed class SearchEngineDriveMaintenance
{
    private static readonly string IndexCacheDir = Path.Combine(Logger.SharedDataDir, "indexes");
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
            var enabledIds = new HashSet<string>(_settings().LocalDrives, StringComparer.OrdinalIgnoreCase);
            var supported = enabledIds.Count == 0
                ? detected
                : detected.Where(d => enabledIds.Contains(VolumeHelper.GetVolumeId(d) ?? string.Empty)).ToList();
            var enabled = new HashSet<string>(supported, StringComparer.OrdinalIgnoreCase);
            var drivesToBuild = new List<string>();

            lock (_indexer.LockObj)
            {
                var current = _indexer.Status.Drives.ToDictionary(d => d.Drive, StringComparer.OrdinalIgnoreCase);
                var next = new List<UsnIndexer.DriveIndexStatus>();
                foreach (var drive in visible)
                    next.Add(DriveMaintenanceHelper.UpdateStatus(drive, detectedSet.Contains(drive), enabled.Contains(drive), IndexCacheDir, current, drivesToBuild, cachedPaths));
                _indexer.Status.Drives = next;
            }

            foreach (var drive in drivesToBuild)
                QueueDriveRebuild(drive);
        }
        catch (Exception ex)
        {
            Logger.Log($"[SearchEngine] Failed to refresh drive statuses: {ex.Message}", LogLevel.Error);
        }
    }

    public UsnIndexer.IndexerStatus BuildStatusSnapshot()
    {
        RefreshDrivesInStatus();
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

        var enabledIds = _settings().LocalDrives;
        var driveId = VolumeHelper.GetVolumeId(drive) ?? string.Empty;
        if (enabledIds.Count > 0 && !enabledIds.Contains(driveId, StringComparer.OrdinalIgnoreCase))
            return false;

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
        var enabledIds = new HashSet<string>(_settings().LocalDrives, StringComparer.OrdinalIgnoreCase);
        var isPresent = detectedSet.Contains(drive);
        var isEnabled = enabledIds.Count == 0 ? isPresent : isPresent && enabledIds.Contains(VolumeHelper.GetVolumeId(drive) ?? string.Empty);
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
        return true;
    }

    public void QueueDriveRebuild(string drive) => QueueDriveRebuild(drive, forceRebuild: false);

    private bool QueueDriveRebuild(string drive, bool forceRebuild)
    {
        lock (_pendingDriveRebuilds)
        {
            if (_isRebuilding())
            {
                Logger.Log($"[SearchEngine] Ignored drive {drive} rebuild request because a full rebuild is running.");
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
        lock (_pendingDriveRebuilds)
            _activeRebuildCts[drive] = cts;
        try
        {
            if (forceRebuild)
                ForceRebuildDrive(drive, cts.Token);
            else
                DriveRecovery.RestoreOrRebuild(_indexer, IndexCacheDir, drive, _token(), QueueDriveRebuild, cts.Token);
        }
        catch (Exception ex)
        {
            Logger.Log($"[SearchEngine] Failed to build drive {drive}: {ex.Message}", LogLevel.Error);
            _indexer.SetDriveState(drive, "failed");
        }
        finally
        {
            lock (_pendingDriveRebuilds)
            {
                _pendingDriveRebuilds.Remove(drive);
                if (_activeRebuildCts.Remove(drive, out var current) && ReferenceEquals(current, cts))
                    cts.Dispose();
            }
            UpdateMaintenanceBusyState();
            _onActivityCompleted();
        }
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
        // Only a journal-backed drive's monitor needs stopping before its own rebuild starts -- see
        // UsnIndexer.RemoveDriveMonitor's own comment on why a non-journal drive deliberately does NOT do
        // this instead.
        if (VolumeHelper.SupportsUsnJournal(drive))
            _indexer.RemoveDriveMonitor(drive);
        var wasCancelled = false;
        var metadata = _indexer.BuildDrives(new[] { drive }, clearExisting: false, cacheDir: IndexCacheDir,
            getToken: _ => token, onDriveCancelled: _ => wasCancelled = true);
        if (metadata.Count == 0)
        {
            // A Stop request reverts to "cached" (mirrors NetworkIndexer's own CancelDrive), not "failed"
            // -- the user asked for this, it isn't an error.
            _indexer.SetDriveState(drive, wasCancelled ? "cached" : "failed");
            return;
        }

        EnsureDriveMonitor(drive, metadata[0].JournalId, metadata[0].NextUsn);
        // The drive's own monitor stayed alive throughout the rebuild (see
        // UsnIndexerExtensions.ApplyFolderChange); if it detected a change it couldn't persist against
        // the doomed old LiveIndex, queue one follow-up refresh so the next walk observes it. A journal
        // drive (monitor already stopped above) never sets this flag in the first place.
        if (_indexer.ConsumeMissedFolderChangeDuringRebuild(drive))
            QueueDriveRebuild(drive);
    }

    private void EnsureDriveMonitor(string drive, ulong journalId, long nextUsn) =>
        DriveMonitorFactory.EnsureMonitor(_indexer, drive, journalId, nextUsn, _token(), QueueDriveRebuild);

    private void PopulateCountsFromCache()
    {
        var totalFiles = 0;
        var totalDirs = 0;

        lock (_indexer.LockObj)
        {
            foreach (var drive in _indexer.Status.Drives)
            {
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
