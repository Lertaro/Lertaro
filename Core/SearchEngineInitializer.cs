using Lertaro.Core.Indexer.Usn;

using Lertaro.Core.DriveMonitoring;
namespace Lertaro.Core;

internal class SearchEngineInitializer
{
    private readonly UsnIndexer _indexer;
    private readonly string _indexCacheDir;
    private readonly Action<string>? _onReindexRequired;

    public SearchEngineInitializer(
        UsnIndexer indexer,
        string indexCacheDir,
        Action<string>? onReindexRequired = null)
    {
        _indexer = indexer;
        _indexCacheDir = indexCacheDir;
        _onReindexRequired = onReindexRequired;
    }

    private void EnsureDriveStatuses(IReadOnlyList<string> detectedDrives, IReadOnlyList<string> enabledDrives)
    {
        var enabled = new HashSet<string>(enabledDrives, StringComparer.OrdinalIgnoreCase);
        var statuses = detectedDrives.Select(d => new UsnIndexer.DriveIndexStatus
        {
            Drive = d,
            Enabled = enabled.Contains(d),
            Kind = VolumeHelper.GetDisplayFileSystemType(d),
            State = enabled.Contains(d) ? "pending" : "disabled",
            CachePath = LocalDriveCacheLocator.GetCachePath(_indexCacheDir, d)
        }).ToList();

        _indexer.SetDriveStatuses(statuses);
    }

    public void Run(bool forceRebuild, CancellationTokenSource cts, Action<bool> onComplete)
    {
        try
        {
            var machineSettings = MachineSettings.Load();
            var detectedDrives = VolumeHelper.DetectIndexableLocalDrives();
            var enabledIds = new HashSet<string>(machineSettings.LocalDrives, StringComparer.OrdinalIgnoreCase);
            var supportedDrives = enabledIds.Count == 0
                ? detectedDrives
                : detectedDrives.Where(d => enabledIds.Contains(VolumeHelper.GetVolumeId(d) ?? string.Empty)).ToList();

            EnsureDriveStatuses(detectedDrives, supportedDrives);

            var loadedFromCache = false;
            List<(string Drive, ulong JournalId, long NextUsn)> cachedMetadata = new();

            if (!forceRebuild)
            {
                Logger.Log("[SearchEngineInitializer] Attempting to load per-drive index caches...");
                lock (_indexer.LockObj)
                {
                    _indexer.Status.State = "loading-cache";
                    _indexer.Status.Progress = 0;
                }

                cachedMetadata = _indexer.LoadDrivesFromCache(_indexCacheDir, supportedDrives);
                if (cachedMetadata.Count > 0)
                {
                    Logger.Log($"[SearchEngineInitializer] Loaded {cachedMetadata.Count}/{supportedDrives.Count} per-drive caches. Instantly unblocking UI for instant search.");

                    lock (_indexer.LockObj)
                    {
                        _indexer.Status.State = "ready";
                        _indexer.Status.Progress = 100;
                        _indexer.Status.ActiveDrives = cachedMetadata.Select(m => m.Drive).ToList();
                    }

                    loadedFromCache = true;
                }
                else
                {
                    Logger.Log("[SearchEngineInitializer] No per-drive caches loaded. Falling back to full scan.", LogLevel.Warn);
                }
            }

            var monitorsToStart = new List<(string Drive, ulong JournalId, long NextUsn)>();

            if (loadedFromCache)
            {
                // Catch up from the cached USN silently in background
                var catchUpSuccess = true;
                var updatedMetadata = new List<(string Drive, ulong JournalId, long NextUsn)>();

                for (var i = 0; i < cachedMetadata.Count; i++)
                {
                    var meta = cachedMetadata[i];
                    // An incomplete cache (a checkpoint from a scan interrupted by a crash/restart before
                    // it finished) must not be mistaken for "nothing more to do" -- leaving it OUT of
                    // updatedMetadata here means it falls into missingDrives below and gets a real rebuild,
                    // same as NetworkIndexer.Configure's own IsComplete-gated resume for network drives.
                    // Its LiveIndex stays in _recordIndexes (nothing here drops it), so that rebuild picks
                    // it up as a TreeDiffBaseline resume point instead of starting fully from scratch.
                    if (!_indexer.IsDriveIndexComplete(meta.Drive))
                        continue;

                    if (!VolumeHelper.SupportsUsnJournal(meta.Drive))
                    {
                        updatedMetadata.Add(meta);
                        continue;
                    }

                    var newUsn = _indexer.CatchUpDrive(meta.Drive, meta.JournalId, meta.NextUsn);

                    if (newUsn < 0)
                    {
                        Logger.Log($"[SearchEngineInitializer] Silent catch-up failed for drive {meta.Drive} (journal mismatch or error). Requiring full re-index.", LogLevel.Error);
                        catchUpSuccess = false;
                        break;
                    }

                    updatedMetadata.Add((meta.Drive, meta.JournalId, newUsn));
                }

                if (catchUpSuccess)
                {
                    Logger.Log("[SearchEngineInitializer] Silent background catch-up completed successfully.");
                    monitorsToStart.AddRange(updatedMetadata);
                    TrySaveDriveCache(updatedMetadata, "catch-up");

                    var loadedDrives = new HashSet<string>(updatedMetadata.Select(m => m.Drive), StringComparer.OrdinalIgnoreCase);
                    var missingDrives = supportedDrives.Where(d => !loadedDrives.Contains(d)).ToList();
                    if (missingDrives.Count > 0)
                    {
                        Logger.Log($"[SearchEngineInitializer] Building missing/incomplete per-drive indices: {string.Join(", ", missingDrives)}");
                        var missingMetadata = _indexer.BuildDrives(missingDrives, clearExisting: false, cacheDir: _indexCacheDir);
                        monitorsToStart.AddRange(missingMetadata);
                    }
                }
                else
                {
                    // Fallback to full reindex
                    loadedFromCache = false;
                    lock (_indexer.LockObj)
                    {
                        _indexer.Status.State = "indexing";
                        _indexer.Status.Progress = 0;
                    }
                }
            }

            if (!loadedFromCache)
            {
                Logger.Log("[SearchEngineInitializer] Building new index from scratch...");
                var newMetadata = _indexer.BuildDrives(supportedDrives, clearExisting: true, cacheDir: _indexCacheDir);
                monitorsToStart = newMetadata;
                lock (_indexer.LockObj)
                {
                    _indexer.Status.State = "ready";
                    _indexer.Status.Progress = 100;
                }
            }

            _indexer.CompactMemory();

            var monitorDrives = new HashSet<string>(monitorsToStart.Select(m => m.Drive), StringComparer.OrdinalIgnoreCase);
            foreach (var drive in supportedDrives)
            {
                if (!monitorDrives.Contains(drive))
                    _indexer.SetDriveState(drive, "failed");
            }

            foreach (var (drive, journalId, nextUsn) in monitorsToStart)
            {
                StartMonitor(drive, journalId, nextUsn, cts.Token);
            }

            Logger.Log($"[SearchEngineInitializer] Started real-time USN monitors for {monitorsToStart.Count(m => VolumeHelper.SupportsUsnJournal(m.Drive))} drives.");
        }
        catch (Exception ex)
        {
            Logger.Log($"[SearchEngineInitializer] Index initialization failed: {ex}", LogLevel.Error);
        }
        finally
        {
            onComplete(false);
        }
    }

    private void TrySaveDriveCache(List<(string Drive, ulong JournalId, long NextUsn)> metadata, string stage)
    {
        if (metadata.Count == 0)
            return;
        try
        {
            _indexer.SaveDrivesToCache(_indexCacheDir, metadata);
        }
        catch (Exception ex)
        {
            Logger.Log($"[SearchEngineInitializer] Failed to save drive cache after {stage}: {ex.Message}", LogLevel.Warn);
        }
    }

    private void StartMonitor(string drive, ulong journalId, long nextUsn, CancellationToken token) =>
        DriveMonitorFactory.EnsureMonitor(_indexer, drive, journalId, nextUsn, token, _onReindexRequired);
}
