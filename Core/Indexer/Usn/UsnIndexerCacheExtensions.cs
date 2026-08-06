using Lertaro.Core.IndexV2;

using Lertaro.Core.IndexV2.Alias;

using Lertaro.Core.IndexV2.Persistence;
namespace Lertaro.Core.Indexer.Usn;

public static class UsnIndexerCacheExtensions
{
    public static void SaveDrivesToCache(
        this UsnIndexer indexer,
        string cacheDir,
        List<(string Drive, ulong JournalId, long NextUsn)> driveMetadata)
    {
        lock (indexer.LockObj)
        {
            IndexCacheManager.SaveDrivesToCache(cacheDir, driveMetadata, indexer._recordIndexes, indexer._driveMetadata);
        }
    }

    public static List<(string Drive, ulong JournalId, long NextUsn)> LoadDrivesFromCache(
        this UsnIndexer indexer,
        string cacheDir,
        IReadOnlyList<string> drives)
    {
        lock (indexer.LockObj)
        {
            indexer._driveMetadata.Clear();
            foreach (var live in indexer._recordIndexes.Values)
                live.Dispose();
            indexer._recordIndexes.Clear();
            indexer.Status.ActiveDrives.Clear();
            indexer.Status.TotalFiles = 0;
            indexer.Status.TotalDirs = 0;
        }

        var loaded = new List<(string Drive, LiveIndex Live, UsnIndexer.DriveRuntimeMetadata Metadata, ulong JournalId, long NextUsn)>();
        foreach (var drive in drives)
        {
            var opened = TryOpenV2(cacheDir, drive);
            if (opened == null)
                continue;

            loaded.Add((drive, opened.Value.Live, opened.Value.Metadata, opened.Value.Metadata.JournalId, opened.Value.Metadata.NextUsn));

            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            Win32Api.TrimWorkingSet();
        }

        lock (indexer.LockObj)
        {
            var metadata = new List<(string Drive, ulong JournalId, long NextUsn)>();
            foreach (var item in loaded)
            {
                indexer._driveMetadata[item.Drive] = item.Metadata;
                indexer._recordIndexes[item.Drive] = item.Live;
                var (files, dirs) = item.Live.GetCounts();
                indexer.Status.TotalFiles += files;
                indexer.Status.TotalDirs += dirs;
                indexer.Status.ActiveDrives.Add(item.Drive);
                metadata.Add((item.Drive, item.JournalId, item.NextUsn));
                indexer.UpdateDriveCounts(item.Drive, markReady: true);
            }

            if (metadata.Count > 0)
            {
                indexer.Status.State = "ready";
                indexer.Status.Progress = 100;
            }

            indexer.CompactMemory();
            return metadata;
        }
    }

    public static (ulong JournalId, long NextUsn)? TryLoadDriveFromCache(
        this UsnIndexer indexer,
        string cacheDir,
        string drive)
    {
        var opened = TryOpenV2(cacheDir, drive);
        if (opened == null)
            return null;
        var (live, metadata) = opened.Value;

        lock (indexer.LockObj)
        {
            indexer._driveMetadata[drive] = metadata;
            indexer._recordIndexes[drive] = live;

            if (!indexer.Status.ActiveDrives.Contains(drive, StringComparer.OrdinalIgnoreCase))
                indexer.Status.ActiveDrives.Add(drive);

            var totals = indexer._recordIndexes.Values.Select(r => r.GetCounts()).ToList();
            indexer.Status.TotalFiles = totals.Sum(t => t.Files);
            indexer.Status.TotalDirs = totals.Sum(t => t.Dirs);
            indexer.Status.State = "ready";
            indexer.Status.Progress = 100;
            indexer.UpdateDriveCounts(drive, markReady: true);
            return (metadata.JournalId, metadata.NextUsn);
        }
    }

    // Whether the drive's currently-loaded runtime metadata came from a genuinely finished scan, as
    // opposed to a mid-walk checkpoint or a scan interrupted before finishing -- the local-drive
    // counterpart of NetworkIndexer.Configure's own IsComplete check, which is what lets network drives
    // automatically resume an interrupted scan on the next cold start instead of silently treating a
    // partial cache as the final answer. False (not just "unknown") for a drive with no loaded metadata
    // at all, so a caller can use this directly without a separate "is it even loaded" check first.
    public static bool IsDriveIndexComplete(this UsnIndexer indexer, string drive)
    {
        lock (indexer.LockObj)
        {
            if (!indexer._driveMetadata.TryGetValue(drive, out var metadata))
                return false;

            // True NTFS ($MFT, via MftIndexScanner) has no partial/checkpoint output at all -- it's always
            // either a fully-finished result or nothing (no result ever gets cached), so its cache is
            // trivially always complete regardless of what IsComplete says. Checking the metadata's own
            // FileSystemType (not a fresh VolumeHelper.GetFileSystemType(drive) call) also means an
            // EXISTING NTFS cache written before this field existed -- IsComplete defaults to false on
            // anything that never explicitly set it -- doesn't trigger a needless full $MFT re-scan the
            // first time this check ships; only ReFS/non-journal drives, which can genuinely be
            // incomplete, pay that one-time cost.
            if (metadata.FileSystemType.Equals("NTFS", StringComparison.OrdinalIgnoreCase))
                return true;

            return metadata.IsComplete;
        }
    }

    public static void DropDriveFromRuntime(this UsnIndexer indexer, string drive)
    {
        // Cancels (not fires) any pending debounced ApplyFolderChange save for this drive -- letting it
        // fire after Dispose() below would call Compact() on an already-disposed LiveIndex.
        indexer._folderChangeSaveDebounce.Cancel(drive);

        lock (indexer.LockObj)
        {
            indexer._driveMetadata.Remove(drive);
            if (indexer._recordIndexes.Remove(drive, out var live))
                live.Dispose();
            indexer.Status.ActiveDrives.RemoveAll(d => d.Equals(drive, StringComparison.OrdinalIgnoreCase));
            var totals = indexer._recordIndexes.Values.Select(r => r.GetCounts()).ToList();
            indexer.Status.TotalFiles = totals.Sum(t => t.Files);
            indexer.Status.TotalDirs = totals.Sum(t => t.Dirs);
        }
    }

    // Opens a drive's V2 cache. No legacy v15 (.meta/.records/.names) migration -- a pre-V2 cache is
    // simply not found here, so the normal "no cache" path in the caller does a full fresh rebuild
    // instead. Returns null if no .idx exists, it fails to open, or the volume identity has changed
    // since the cache was written (IsCurrentVolumeCache).
    private static (LiveIndex Live, UsnIndexer.DriveRuntimeMetadata Metadata)? TryOpenV2(string cacheDir, string drive)
    {
        var v2Path = LocalDriveCacheLocator.GetCachePath(cacheDir, drive);
        if (!File.Exists(v2Path))
            return null;

        Snapshot snapshot;
        try
        {
            snapshot = Snapshot.Open(v2Path);
        }
        catch (Exception ex)
        {
            Logger.Log($"[UsnIndexer] Failed to open IndexV2 cache for drive {drive}: {ex.Message}", LogLevel.Error);
            return null;
        }

        var metadata = new UsnIndexer.DriveRuntimeMetadata
        {
            SourceKind = snapshot.SourceKind,
            IdKind = snapshot.IdKind,
            FileSystemType = snapshot.FileSystemType,
            VolumeSerialNumber = snapshot.VolumeSerialNumber,
            RootId = snapshot.RootId,
            JournalId = snapshot.JournalId,
            NextUsn = snapshot.NextUsn,
            IsComplete = snapshot.IsComplete,
        };
        if (!IsCurrentVolumeCache(drive, metadata))
        {
            snapshot.Dispose();
            return null;
        }
        var live = new LiveIndex(snapshot);
        AliasProvidersReconciler.ReconcileIfStale(live, v2Path, drive);
        return (live, metadata);
    }

    private static bool IsCurrentVolumeCache(string drive, UsnIndexer.DriveRuntimeMetadata metadata)
    {
        if (metadata.SourceKind != FileRecordSourceKind.LocalMft)
            return true;

        var identity = VolumeHelper.GetVolumeIdentity(drive);
        if (!identity.HasValue)
        {
            Logger.Log($"[UsnIndexer] Ignoring cached index for drive {drive}: volume identity unavailable.", LogLevel.Warn);
            return false;
        }

        var current = identity.Value;
        var matches = metadata.VolumeSerialNumber == current.SerialNumber &&
            metadata.FileSystemType.Equals(current.FileSystemType, StringComparison.OrdinalIgnoreCase);
        if (!matches)
        {
            Logger.Log($"[UsnIndexer] Ignoring cached index for drive {drive}: volume identity changed.", LogLevel.Warn);
        }

        return matches;
    }
}
