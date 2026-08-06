using Lertaro.Core.IndexV2;

using Lertaro.Core.IndexV2.Persistence;
using Lertaro.Core.Indexer.Usn.Journal;
namespace Lertaro.Core.Indexer.Usn;

public static class UsnIndexerBuildExtensions
{
    public static List<(string Drive, ulong JournalId, long NextUsn)> BuildIndex(this UsnIndexer indexer)
    {
        Logger.Log("[UsnIndexer] BuildIndex started");
        var drives = VolumeHelper.DetectIndexableLocalDrives();
        Logger.Log($"[UsnIndexer] Detected NTFS/ReFS drives: {string.Join(", ", drives)}");
        return indexer.BuildDrives(drives, clearExisting: true);
    }

    public static List<(string Drive, ulong JournalId, long NextUsn)> BuildDrives(
        this UsnIndexer indexer,
        IReadOnlyList<string> drives,
        bool clearExisting,
        string? cacheDir = null,
        Func<string, CancellationToken>? getToken = null,
        Action<string>? onDriveCancelled = null)
    {
        lock (indexer.LockObj)
        {
            if (clearExisting)
            {
                indexer.Status.State = "indexing";
                indexer.Status.Progress = 0;
            }
            if (clearExisting)
            {
                indexer.Status.TotalFiles = 0;
                indexer.Status.TotalDirs = 0;
                indexer._driveMetadata.Clear();
                foreach (var live in indexer._recordIndexes.Values)
                    live.Dispose();
                indexer._recordIndexes.Clear();
                indexer.Status.ActiveDrives.Clear();
            }

            if (clearExisting)
                indexer.Status.ActiveDrives = drives.ToList();
            else
            {
                foreach (var drive in drives)
                {
                    if (!indexer.Status.ActiveDrives.Contains(drive))
                        indexer.Status.ActiveDrives.Add(drive);
                    indexer.SetDriveState(drive, "indexing");
                }
            }
        }

        // IndexV2 has no pure in-memory construction path (mmap is the only runtime representation),
        // so a cache directory is required -- every production call site already supplies one
        // (SearchEngineInitializer, DriveRecovery, SearchEngineDriveMaintenance); the parameterless
        // cacheDir default only existed for the old in-memory RuntimeIndex.Load path and is unused.
        if (string.IsNullOrWhiteSpace(cacheDir))
        {
            Logger.Log("[UsnIndexer] BuildDrives requires a cache directory under IndexV2; no drives will be built.", LogLevel.Error);
            return new List<(string, ulong, long)>();
        }

        return IndexBuilder.BuildDrives(
            indexer._reader,
            drives,
            indexer.SetDriveState,
            indexer.UpdateDriveProgress,
            (drive, onProgress, token) =>
            {
                FileRecordStore? previousStore;
                lock (indexer.LockObj)
                    previousStore = indexer._recordIndexes.TryGetValue(drive, out var live) ? live.ToStore() : null;
                return LocalDriveWalkBuilder.Build(drive, $"{drive}:\\", previousStore, onProgress ?? ((_, _) => { }), token,
                    onCheckpoint: (checkpointStore, _) => indexer.PublishLocalDriveCheckpoint(cacheDir, drive, checkpointStore, token));
            },
            (drive, result, progress, index) => OnDriveCompleted(indexer, cacheDir, drive, result, progress),
            (drive, result, progress, index) => OnDriveCompleted(indexer, cacheDir, drive, result.Store, progress),
            elapsedSeconds =>
            {
                lock (indexer.LockObj)
                {
                    indexer.Status.State = "ready";
                    indexer.Status.Progress = 100;
                    indexer.Status.ElapsedTime = elapsedSeconds;
                }
                Logger.Log($"[UsnIndexer] All indices built! Files: {indexer.Status.TotalFiles}, Folders: {indexer.Status.TotalDirs}. Time: {elapsedSeconds:F2}s");

                GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
                Win32Api.TrimWorkingSet();
            },
            getToken,
            onDriveCancelled,
            // Feeds ReFsScanner's own diff-reuse (see JournalReader.IndexDrive) -- a no-op for NTFS/$MFT
            // and never called for a non-journal drive (that's buildFolderDrive's own inline lookup above).
            drive =>
            {
                lock (indexer.LockObj)
                    return indexer._recordIndexes.TryGetValue(drive, out var live) ? live.ToStore() : null;
            },
            // ReFsScanner's own mid-walk checkpoint -- reuses the same publisher the non-USN local-drive
            // path above does (PublishLocalDriveCheckpoint doesn't care about SourceKind/IdKind, only about
            // writing a store and swapping a fresh LiveIndex in). A no-op for NTFS/$MFT (see JournalReader).
            (drive, checkpointStore, _, token) => indexer.PublishLocalDriveCheckpoint(cacheDir, drive, checkpointStore, token)
        );
    }

    // Writes the just-scanned store straight to its V2 cache file, THEN frees the in-memory
    // List<FileRecord> before mapping the file back in -- same "don't hold both the raw scan AND the
    // loaded index in memory at once" shape the old engine's LoadFromCacheDirect round-trip had, now
    // free (mmap opening is O(1), not a parse) instead of merely cheaper.
    private static void OnDriveCompleted(UsnIndexer indexer, string cacheDir, string drive, FileRecordStore store, int progress)
    {
        indexer.DropDriveFromRuntime(drive);

        var path = LocalDriveCacheLocator.GetCachePath(cacheDir, drive);
        SnapshotWriter.Write(store, path);
        var metadata = UsnIndexer.CreateMetadata(store);
        store.Records.Clear();
        store.Records.TrimExcess();

        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        Win32Api.TrimWorkingSet();

        var live = new LiveIndex(Snapshot.Open(path));

        lock (indexer.LockObj)
        {
            indexer._driveMetadata[drive] = metadata;
            indexer._recordIndexes[drive] = live;
            var totals = indexer._recordIndexes.Values.Select(r => r.GetCounts()).ToList();
            indexer.Status.TotalFiles = totals.Sum(t => t.Files);
            indexer.Status.TotalDirs = totals.Sum(t => t.Dirs);
            indexer.Status.Progress = progress;
            indexer.UpdateDriveCounts(drive, markReady: true);
        }
    }
}
