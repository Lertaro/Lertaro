using Lertaro.Core.IndexV2;
using Lertaro.Core.IndexV2.Delta;
using Lertaro.Core.DriveMonitoring;
using Lertaro.Core.Indexer.NetworkDrive.Walk;
namespace Lertaro.Core.Indexer.Usn;

public static class UsnIndexerExtensions
{
    // Reasons that mean the file's data/attributes changed in a way that can affect Size or the three
    // tracked timestamps. None of these carry the actual values on the USN record itself, so handling
    // them means an extra re-stat, unlike the name-index reasons above which the record already covers.
    // FILE_CREATE and RENAME_NEW_NAME are included too: a fresh link always lands with Size/timestamps
    // defaulted to zero, whether or not a sibling row for the same FRN already had real stat data -- a
    // create-and-immediately-write burst or a rename-with-attribute-change happens to carry a
    // DATA_*/BASIC_INFO_CHANGE reason in the same record and self-corrects, but a file created and left
    // empty, or a plain rename/move with no other change, doesn't. Left unhandled, that file's Size and
    // Creation/LastWrite/LastAccess time all stay at zero, which GetRecentFiles reads as "created at
    // the Unix epoch" and filters out of every age-windowed query.
    private const uint MetadataRefreshReasons = Win32Api.USN_REASON_FILE_CREATE | Win32Api.USN_REASON_RENAME_NEW_NAME
        | Win32Api.USN_REASON_DATA_EXTEND | Win32Api.USN_REASON_DATA_OVERWRITE | Win32Api.USN_REASON_DATA_TRUNCATION
        | Win32Api.USN_REASON_BASIC_INFO_CHANGE | Win32Api.USN_REASON_COMPRESSION_CHANGE | Win32Api.USN_REASON_ENCRYPTION_CHANGE;

    public static long CatchUpDrive(this UsnIndexer indexer, string drive, ulong journalId, long startUsn)
    {
        var changes = new List<ParsedUsnRecord>();
        var nextUsn = indexer._reader.CatchUpDrive(drive, journalId, startUsn, changes.Add);
        if (nextUsn >= 0 && changes.Count > 0)
            indexer.ApplyUsnRecords(drive, changes);

        return nextUsn;
    }

    public static void ApplyUsnRecord(this UsnIndexer indexer, string drive, ParsedUsnRecord record)
        => indexer.ApplyUsnRecords(drive, new[] { record });

    public static void ApplyUsnRecords(this UsnIndexer indexer, string drive, IReadOnlyList<ParsedUsnRecord> records)
    {
        Logger.Log($"[UsnIndexer] Applying {records.Count} USN records to drive {drive}", LogLevel.Debug);

        LiveIndex? live;
        lock (indexer.LockObj)
        {
            if (!indexer._recordIndexes.TryGetValue(drive, out live))
                return;
        }

        var namePool = new FileRecordNamePool();
        var pendingMetadataFrns = new HashSet<UInt128>();
        // Collected here rather than derived afterwards from the delta: the record names its parent
        // directly, so this costs a hash insert per record and no path work at all for the (many)
        // batches that turn out to be several changes in the same folder.
        var changedParentFrns = new HashSet<UInt128>();

        // One Mutate call for the whole batch -- LiveIndex's write lock makes the batch atomic with
        // respect to concurrent searches on this drive (a search never sees half the batch applied).
        live.Mutate((snapshot, delta) =>
        {
            foreach (var record in records)
            {
                // One-to-many: operate on the exact link the record names (FRN, parent, name), so
                // renaming/deleting/creating one hard link never disturbs the file's other links.
                var frn = record.FileReferenceNumber;
                var parentFrn = record.ParentFileReferenceNumber;
                var linkName = namePool.Get(record.FileName);
                var linkFlags = FileRecordFlagsHelper.FromAttributes((FileAttributes)record.FileAttributes);
                changedParentFrns.Add(parentFrn);
                if ((record.FileAttributes & (uint)FileAttributes.Directory) != 0)
                    changedParentFrns.Add(frn);

                if ((record.Reason & Win32Api.USN_REASON_HARD_LINK_CHANGE) != 0
                    && (record.Reason & (Win32Api.USN_REASON_FILE_CREATE | Win32Api.USN_REASON_FILE_DELETE)) == 0)
                {
                    DeltaLinkOps.ToggleLink(delta, frn, parentFrn, linkName, linkFlags);
                }
                else if ((record.Reason & Win32Api.USN_REASON_RENAME_OLD_NAME) != 0)
                {
                    // Unlike a real delete, the FRN survives under a new name (RENAME_NEW_NAME follows),
                    // so a directory's children must not cascade-remove here.
                    DeltaLinkOps.RemoveLinkForRename(delta, frn, parentFrn, linkName);
                }
                else if ((record.Reason & Win32Api.USN_REASON_FILE_DELETE) != 0)
                {
                    DeltaLinkOps.RemoveLink(delta, frn, parentFrn, linkName);
                }
                else if ((record.Reason & (Win32Api.USN_REASON_FILE_CREATE | Win32Api.USN_REASON_RENAME_NEW_NAME)) != 0)
                {
                    DeltaLinkOps.AddLink(delta, frn, parentFrn, linkName, linkFlags);
                }

                // Unlike name-index changes, Size/timestamps are never carried by the USN record itself
                // (USN_RECORD has no such fields), so any of these reasons -- including a plain create --
                // needs an actual re-stat. Just collect which FRNs need it here; the actual I/O happens
                // after this call returns.
                if ((record.Reason & MetadataRefreshReasons) != 0 && (record.Reason & Win32Api.USN_REASON_FILE_DELETE) == 0)
                {
                    pendingMetadataFrns.Add(frn);
                    DeltaLinkOps.UpdateFlags(delta, frn, linkFlags);
                }
            }
        });

        // Resolved before taking LockObj, never inside it: reading a path takes the LiveIndex's own
        // lock, and taking the two in this order here and the other order anywhere else is a deadlock.
        var changedDirectories = UsnIndexerChangedDirectories.Resolve(live, changedParentFrns);

        lock (indexer.LockObj)
        {
            indexer.UpdateTotalsFromRuntime();
            indexer.UpdateDriveCounts(drive);
        }
        // Outside the lock: a subscriber matching this against its own watch list has no business
        // holding up the next batch, which is typically microseconds away.
        indexer.RaiseDirectoriesChanged(drive, changedDirectories);
        SearchCoordinator.ClearCaches();

        // Stat outside any lock: a write-heavy burst (build, bulk copy) can touch hundreds of distinct
        // files in one 64KB journal buffer, and holding a lock for that many disk stats would serialize
        // this drive's searches/updates behind the whole batch.
        if (pendingMetadataFrns.Count > 0)
            RefreshMetadata(live, pendingMetadataFrns);

        indexer.PublishStatusChanged();
    }

    private static void RefreshMetadata(LiveIndex live, HashSet<UInt128> frns)
    {
        // Path lookups need the read lock (DeltaOverlay's dictionaries aren't safe to read without it,
        // unlike the old engine's bespoke concurrent collections) -- but that's cheap; the disk I/O
        // below runs with no lock held at all.
        var paths = live.Read((snapshot, delta) =>
        {
            var map = new Dictionary<UInt128, string>(frns.Count);
            foreach (var frn in frns)
                if (delta.TryGetPathForFrn(frn, out var path))
                    map[frn] = path;
            return map;
        });

        var results = new List<(UInt128 Frn, long Size, uint CreationTimeUtc, uint LastWriteTimeUtc, uint LastAccessTimeUtc)>(paths.Count);
        foreach (var (frn, path) in paths)
        {
            FileInfo info;
            try
            {
                info = new FileInfo(path);
                if (!info.Exists)
                    continue;
            }
            catch
            {
                continue;
            }

            var isDirectory = (info.Attributes & FileAttributes.Directory) != 0;
            results.Add((frn, isDirectory ? 0 : info.Length,
                FileTimeHelper.ToUnixSeconds(info.CreationTimeUtc), FileTimeHelper.ToUnixSeconds(info.LastWriteTimeUtc), FileTimeHelper.ToUnixSeconds(info.LastAccessTimeUtc)));
        }

        if (results.Count == 0)
            return;

        live.Mutate((snapshot, delta) =>
        {
            foreach (var result in results)
                DeltaLinkOps.UpdateMetadata(delta, result.Frn, result.Size, result.CreationTimeUtc, result.LastWriteTimeUtc, result.LastAccessTimeUtc);
        });
    }

    public static void ApplyFolderChange(this UsnIndexer indexer, string drive, WatcherChangeTypes changeType, string path, string? oldPath = null)
    {
        LiveIndex? live;
        lock (indexer.LockObj)
        {
            if (!indexer._recordIndexes.TryGetValue(drive, out live))
            {
                // A rebuild in progress for this drive briefly removes its old LiveIndex from this
                // dictionary before the fresh one is swapped in (see
                // UsnIndexerBuildExtensions.OnDriveCompleted) -- a change landing in that narrow window
                // has nothing left to apply to, but must still be flagged as missed rather than silently
                // dropped, same as the two cases below. Already inside the lock this method needs.
                MarkMissedIfRebuilding(indexer, drive);
                return;
            }
        }

        var root = $"{drive}:\\";
        var isDirectory = Directory.Exists(path);
        var normalizedPath = PathHelpers.NormalizePath(path, isDirectory);
        var changed = false;

        try
        {
            live.Mutate((snapshot, delta) => changed = changeType switch
            {
                WatcherChangeTypes.Deleted => DeltaPathApplier.ApplyDeleted(delta, normalizedPath),
                WatcherChangeTypes.Renamed when !string.IsNullOrWhiteSpace(oldPath) => DeltaPathApplier.ApplyRenamed(delta, (UInt128)1, root, oldPath, normalizedPath),
                _ => DeltaPathApplier.ApplyCreatedOrChanged(delta, (UInt128)1, root, normalizedPath),
            });
        }
        catch (ObjectDisposedException)
        {
            // `live` was disposed by a concurrent rebuild finishing for this same drive in the narrow,
            // unlocked window between the TryGetValue lookup above and this call (LiveIndex.Mutate's own
            // write lock throws once LiveIndex.Dispose has already torn it down) -- keeping this drive's
            // monitor running through its own rebuild (rather than stopping it beforehand) makes this
            // race newly reachable. Nothing left to apply this change to; flag it as missed instead of
            // letting the exception escape into whatever thread the FolderDriveMonitor callback runs on.
            lock (indexer.LockObj)
                MarkMissedIfRebuilding(indexer, drive);
            return;
        }
        if (!changed)
            return;

        // A rename moves something out of one directory and into another, so both are places a
        // subscriber watching either one needs to hear about.
        var changedDirectories = UsnIndexerChangedDirectories.ForPath(normalizedPath, isDirectory);
        if (changeType == WatcherChangeTypes.Renamed && !string.IsNullOrWhiteSpace(oldPath))
            changedDirectories.AddRange(UsnIndexerChangedDirectories.ForPath(oldPath, isDirectory: false));

        bool isRebuilding;
        lock (indexer.LockObj)
        {
            indexer.UpdateTotalsFromRuntime();
            indexer.UpdateDriveCounts(drive);
            isRebuilding = MarkMissedIfRebuilding(indexer, drive);
        }
        indexer.RaiseDirectoriesChanged(drive, changedDirectories);
        SearchCoordinator.ClearCaches();
        // While this drive is being rebuilt, its FolderDriveMonitor stays alive (mirroring
        // NetworkIndexerPublisher/WatcherManager's own approach for network/WSL/folder-index drives)
        // and keeps applying changes to THIS old LiveIndex's in-memory delta above -- immediately
        // searchable -- but persisting here would race the rebuild's own SnapshotWriter.Write to the
        // same cache path, and this LiveIndex is about to be disposed and replaced wholesale anyway
        // (see UsnIndexerBuildExtensions.OnDriveCompleted). The missed flag set above lets the
        // rebuild's own caller queue one follow-up refresh once it finishes, so the fresh walk gets a
        // chance to observe whatever was missed on its own -- see ConsumeMissedFolderChangeDuringRebuild.
        //
        // Otherwise: SaveDriveSnapshot does a synchronous FULL Compact(force: true) -- calling it on
        // every single debounced-batch item from FolderDriveMonitor meant one full-index rewrite per
        // changed file, same unthrottled cost WatcherManager had for network/WSL/folder-index drives.
        // Only the expensive disk persist is debounced per drive, so a burst of changes collapses into
        // one rewrite once that drive goes quiet for a bit.
        if (!isRebuilding)
            indexer._folderChangeSaveDebounce.Schedule(drive, () => indexer.SaveDriveSnapshot(drive, live));
        indexer.PublishStatusChanged();
    }

    // Caller must already hold indexer.LockObj. Flags `drive` as having missed a change during its
    // current rebuild (if State == "indexing" right now) and returns whether it did -- shared by
    // ApplyFolderChange's three "nothing to apply this change to" cases (drive not tracked at all,
    // caught a concurrent LiveIndex disposal, or applied successfully but the drive turned out to be
    // rebuilding) and by the normal in-progress path that still needs the bool to decide whether to skip
    // the persist below.
    private static bool MarkMissedIfRebuilding(UsnIndexer indexer, string drive)
    {
        var item = indexer.Status.Drives.FirstOrDefault(d => d.Drive.Equals(drive, StringComparison.OrdinalIgnoreCase));
        var isRebuilding = item != null && item.State == "indexing";
        if (isRebuilding)
            indexer._missedFolderChangeDuringRebuild.Add(drive);
        return isRebuilding;
    }

    // Consumes (clears) the "a change was detected while this drive was being rebuilt" flag set by
    // ApplyFolderChange above. Called once by the rebuild's own caller (SearchEngineDriveMaintenance/
    // DriveRecovery) right after BuildDrives returns and the fresh LiveIndex is already swapped in and
    // marked ready, so a true result only ever reflects a change that's otherwise unrecoverable -- one
    // that arrived after the swap already applied normally to the NEW index and scheduled its own
    // persist, so it never sets this flag in the first place.
    public static bool ConsumeMissedFolderChangeDuringRebuild(this UsnIndexer indexer, string drive)
    {
        lock (indexer.LockObj)
            return indexer._missedFolderChangeDuringRebuild.Remove(drive);
    }

    public static void SaveDriveSnapshot(this UsnIndexer indexer, string drive, LiveIndex live)
    {
        UsnIndexer.DriveRuntimeMetadata? metadata;
        lock (indexer.LockObj)
        {
            if (!indexer._driveMetadata.TryGetValue(drive, out metadata))
                return;
        }

        var cacheDir = Path.Combine(Logger.UserDataDir, "indexes");
        try
        {
            live.Compact(LocalDriveCacheLocator.GetCachePath(cacheDir, drive), new CompactionStamp(metadata.JournalId, metadata.NextUsn), force: true);
        }
        catch (ObjectDisposedException)
        {
            // This runs on a KeyedDebouncer Timer callback up to a second after ApplyFolderChange
            // scheduled it -- DropDriveFromRuntime's own Cancel only stops a timer that hasn't fired yet;
            // one already mid-flight (past the dictionary lookup above, about to call Compact) when a
            // rebuild's Dispose() runs isn't stopped by it. Nothing to persist: a rebuild replacing this
            // drive's LiveIndex already supersedes whatever this stale save would have written.
        }
    }
}
