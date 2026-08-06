using Lertaro.Core.IndexV2;

using Lertaro.Core.Indexer.Usn.Journal;
namespace Lertaro.Core.Indexer.Usn;

internal static class IndexCacheManager
{
    public static FileRecordStore CreateEmptyStore(
        string drive,
        UInt128 rootFrn,
        long nextUsn,
        ulong journalId)
    {
        var store = new FileRecordStore
        {
            SourceKey = drive,
            SourceKind = FileRecordSourceKind.LocalMft,
            IdKind = FileRecordIdKind.MftFrn,
            RootId = rootFrn,
            JournalId = journalId,
            NextUsn = nextUsn
        };
        var identity = VolumeHelper.GetVolumeIdentity(drive);
        if (identity.HasValue)
        {
            store.FileSystemType = identity.Value.FileSystemType;
            store.VolumeSerialNumber = identity.Value.SerialNumber;
        }

        // Real root mtime plus Listed: by the time any caller of this method has a completed store to
        // return, the root's own children were necessarily gathered one way or another (freshly
        // enumerated or reused from a previous pass) -- see TreeDiffBaseline.TryGetUnchangedChildren,
        // which ReFsScanner now consults using both of these.
        var rootLastWriteTime = FileTimeHelper.TryGetLastWriteTimeUnixSeconds($"{drive}:\\");

        store.Records.Add(new FileRecord(
            store.RootId,
            store.RootId,
            string.Empty,
            FileRecordFlags.Directory | FileRecordFlags.SourceRoot | FileRecordFlags.Listed,
            lastWriteTimeUnixSeconds: rootLastWriteTime));
        return store;
    }

    public static FileRecordStore CreateStoreFromDriveData(
        string drive,
        UInt128 rootFrn,
        Dictionary<UInt128, ReFsItem> searchItems,
        long nextUsn,
        ulong journalId)
    {
        var store = CreateEmptyStore(drive, rootFrn, nextUsn, journalId);
        var namePool = new FileRecordNamePool();

        foreach (var kvp in searchItems)
        {
            var flags = (kvp.Value.IsDir ? FileRecordFlags.Directory : FileRecordFlags.None)
                | (kvp.Value.Listed ? FileRecordFlags.Listed : FileRecordFlags.None);
            store.Records.Add(new FileRecord(
                kvp.Key,
                kvp.Value.ParentFrn,
                namePool.Get(kvp.Value.Name),
                flags,
                kvp.Value.Size,
                FileTimeHelper.FileTimeToUnixSeconds(kvp.Value.CreationTimeUtc),
                FileTimeHelper.FileTimeToUnixSeconds(kvp.Value.LastWriteTimeUtc),
                FileTimeHelper.FileTimeToUnixSeconds(kvp.Value.LastAccessTimeUtc)));
        }

        return store;
    }

    // force:true throughout -- matches the old engine's SaveDrivesToCache, which always wrote a full
    // serialization whenever called (callers already gate WHETHER to call this, e.g. only after a
    // catch-up actually ran); a periodic idle-triggered compactor is a different call site that can
    // afford to skip when nothing changed (LiveIndex.Compact's force:false path).
    public static void SaveDrivesToCache(
        string cacheDir,
        List<(string Drive, ulong JournalId, long NextUsn)> driveMetadata,
        IReadOnlyDictionary<string, LiveIndex> recordIndexes,
        IReadOnlyDictionary<string, UsnIndexer.DriveRuntimeMetadata> driveMetadataMap)
    {
        foreach (var meta in driveMetadata)
        {
            if (!driveMetadataMap.TryGetValue(meta.Drive, out var metadata))
                continue;

            if (recordIndexes.TryGetValue(meta.Drive, out var live))
            {
                metadata.JournalId = meta.JournalId;
                metadata.NextUsn = meta.NextUsn;
                live.Compact(LocalDriveCacheLocator.GetCachePath(cacheDir, meta.Drive), new CompactionStamp(meta.JournalId, meta.NextUsn), force: true);
            }
        }
    }
}
