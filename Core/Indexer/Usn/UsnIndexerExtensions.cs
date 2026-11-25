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
        | Win32Api.USN_REASON_BASIC_INFO_CHANGE | Win32Api.USN_REASON_COMPRESSION_CHANGE | Win32Api.USN_REASON_ENCRYPTION_CHANGE
        | Win32Api.USN_REASON_HARD_LINK_CHANGE | Win32Api.USN_REASON_REPARSE_POINT_CHANGE;
    private const uint AttributeRefreshReasons = MetadataRefreshReasons;
    private const uint DirectoryChangeReasons = MetadataRefreshReasons | Win32Api.USN_REASON_RENAME_OLD_NAME
        | Win32Api.USN_REASON_FILE_DELETE;
    // Returns whether the batch landed in this drive's live index. UsnMonitor needs that to decide
    // whether the drive's durable journal watermark may advance (see UsnIndexerDurabilityExtensions):
    // a batch with nowhere to go must never be stamped as applied, or the next cold start replays from
    // past it and the changes it described are lost for good. True means "applied", NOT "every record
    // matched a row" -- an unmatched removal is reported separately below.
    // `origin` decides how much the batch may spend (see UsnApplyOrigin): a replay skips the per-record
    // re-stat and the per-batch notification fan-out a live stream needs.
    public static bool ApplyUsnRecord(this UsnIndexer indexer, string drive, ParsedUsnRecord record)
        => indexer.ApplyUsnRecords(drive, new[] { record });

    public static bool ApplyUsnRecords(this UsnIndexer indexer, string drive, IReadOnlyList<ParsedUsnRecord> records,
        UsnApplyOrigin origin = UsnApplyOrigin.Live)
    {
        Logger.Log($"[UsnIndexer] Applying {records.Count} USN records to drive {drive}", LogLevel.Debug);
        var isReplay = origin == UsnApplyOrigin.Replay;

        LiveIndex? live;
        lock (indexer.LockObj)
        {
            if (!indexer._recordIndexes.TryGetValue(drive, out live))
            {
                // Nothing to apply to: the drive's LiveIndex is briefly absent while a rebuild owns it
                // (OnDriveCompleted drops the old one, then writes/GC's/reopens the fresh one outside the
                // lock) or after a PNP unload. Flag the same way ApplyFolderChange does when a rebuild is
                // running, and pin the watermark so no later persist stamps past what was dropped here.
                var rebuildOwnsDrive = MarkMissedIfRebuilding(indexer, drive);
                indexer.PinJournalWatermark(drive);
                Logger.Log($"[UsnIndexer] Dropped {records.Count} USN record(s) for drive {drive}: no live index ({(rebuildOwnsDrive ? "a rebuild owns this drive" : "the drive is unloaded")}); its journal watermark is pinned so the next cold start replays them.", LogLevel.Warn);
                return false;
            }
        }

        var namePool = new FileRecordNamePool();
        var pendingMetadataFrns = new HashSet<UInt128>();
        var hardLinks = new List<ParsedUsnRecord>();
        // Removal records that matched no link the index held. Benign when a cold-start catch-up replays
        // a delete this session already applied (which idle compaction deliberately allows, see
        // UsnIndexerDurabilityExtensions.CompactIdleDeltas), but otherwise exactly how a stale row is
        // born -- so it is counted and reported rather than dropped on the floor.
        var unmatchedRemovals = 0;
        // Collected here rather than derived afterwards from the delta: the record names its parent
        // directly, so this costs a hash insert per record and no path work at all for the (many)
        // batches that turn out to be several changes in the same folder.
        var changedParentFrns = new HashSet<UInt128>();

        // Apply the journal's explicit namespace changes under one write lock. Ambiguous hard-link
        // events and metadata are reconciled afterward, keeping filesystem I/O outside that lock.
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
                // Only a record that actually introduced a link needs a stat below. During a replay this
                // is what separates the handful of genuinely new objects from the thousands of records
                // that merely re-describe rows the scan already recorded with real metadata.
                var addedLink = false;
                if ((record.Reason & DirectoryChangeReasons) != 0)
                {
                    changedParentFrns.Add(parentFrn);
                    if ((record.FileAttributes & (uint)FileAttributes.Directory) != 0)
                        changedParentFrns.Add(frn);
                }

                if ((record.Reason & Win32Api.USN_REASON_HARD_LINK_CHANGE) != 0
                    && (record.Reason & (Win32Api.USN_REASON_FILE_CREATE | Win32Api.USN_REASON_FILE_DELETE)) == 0)
                {
                    hardLinks.Add(record);
                }
                else if ((record.Reason & Win32Api.USN_REASON_RENAME_OLD_NAME) != 0)
                {
                    // Unlike a real delete, the FRN survives under a new name (RENAME_NEW_NAME follows),
                    // so a directory's children must not cascade-remove here.
                    if (!DeltaLinkOps.RemoveLinkForRename(delta, frn, parentFrn, linkName))
                        unmatchedRemovals++;
                }
                else if ((record.Reason & Win32Api.USN_REASON_FILE_DELETE) != 0)
                {
                    if (!DeltaLinkOps.RemoveLink(delta, frn, parentFrn, linkName))
                        unmatchedRemovals++;
                }
                else if ((record.Reason & (Win32Api.USN_REASON_FILE_CREATE | Win32Api.USN_REASON_RENAME_NEW_NAME)) != 0)
                {
                    addedLink = DeltaLinkOps.AddLink(delta, frn, parentFrn, linkName, linkFlags);
                }

                // Unlike name-index changes, Size/timestamps are never carried by the USN record itself
                // (USN_RECORD has no such fields), so any of these reasons -- including a plain create --
                // needs an actual re-stat. Just collect which FRNs need it here; the actual I/O happens
                // after this call returns. A replay only pays for the links it introduced: re-statting
                // what the scan just recorded is pure I/O, while skipping a genuinely new file would
                // leave its Size/timestamps at zero, which GetRecentFiles reads as "created at the Unix
                // epoch" and filters out of every age-windowed query.
                if ((record.Reason & MetadataRefreshReasons) != 0 && (record.Reason & Win32Api.USN_REASON_FILE_DELETE) == 0
                    && (!isReplay || addedLink))
                {
                    pendingMetadataFrns.Add(frn);
                }
                if ((record.Reason & AttributeRefreshReasons) != 0 && (record.Reason & Win32Api.USN_REASON_FILE_DELETE) == 0)
                    DeltaLinkOps.UpdateFlags(delta, frn, linkFlags);
            }
        });

        if (unmatchedRemovals > 0)
            Logger.Log($"[UsnIndexer] {drive}: {unmatchedRemovals} of {records.Count} USN removal record(s) matched no indexed link (harmless for a replayed delete; otherwise the rows they named stay visible until a rebuild)",
                isReplay ? LogLevel.Debug : LogLevel.Warn);

        UsnHardLinkReconciler.Apply(live, hardLinks);
        // Child changes also update parent directory metadata without a separate parent USN record. A
        // replay skips that just like it skips the per-record stats: every parent it names is a row the
        // scan behind it already recorded.
        if (!isReplay)
            pendingMetadataFrns.UnionWith(changedParentFrns);

        // Resolved before taking LockObj, never inside it: reading a path takes the LiveIndex's own
        // lock, and taking the two in this order here and the other order anywhere else is a deadlock.
        // A replay resolves nothing: it raises one notification for the whole catch-up instead of
        // re-reading directories once per batch (see UsnIndexerCatchUpExtensions).
        var changedDirectories = isReplay
            ? null
            : UsnIndexerChangedDirectories.Resolve(live, changedParentFrns, drive);

        lock (indexer.LockObj)
        {
            indexer.UpdateTotalsFromRuntime();
            indexer.UpdateDriveCounts(drive);
        }
        // Outside the lock: a subscriber matching this against its own watch list has no business
        // holding up the next batch, which is typically microseconds away.
        if (!isReplay && (changedDirectories == null || changedDirectories.Count > 0))
        {
            indexer.RaiseDirectoriesChanged(drive, changedDirectories);
            SearchCoordinator.ClearCaches();
        }

        // Stat outside any lock: a write-heavy burst (build, bulk copy) can touch hundreds of distinct
        // files in one 64KB journal buffer, and holding a lock for that many disk stats would serialize
        // this drive's searches/updates behind the whole batch.
        if (pendingMetadataFrns.Count > 0)
            UsnMetadataReader.Refresh(live, pendingMetadataFrns);

        indexer.PublishStatusChanged();
        return true;
    }

    public static void ApplyFolderChange(this UsnIndexer indexer, string drive, WatcherChangeTypes changeType, string path, string? oldPath = null)
    {
        var root = $"{drive}:\\";
        var isDirectory = Directory.Exists(path);
        var normalizedPath = PathHelpers.NormalizePath(path, isDirectory);
        var changed = false;
        var pending = MutateFolderDrive(indexer, drive, delta => changed = changeType switch
        {
            WatcherChangeTypes.Deleted => DeltaPathApplier.ApplyDeleted(delta, normalizedPath),
            WatcherChangeTypes.Renamed when !string.IsNullOrWhiteSpace(oldPath) => DeltaPathApplier.ApplyRenamed(delta, (UInt128)1, root, oldPath, normalizedPath),
            _ => DeltaPathApplier.ApplyCreatedOrChanged(delta, (UInt128)1, root, normalizedPath, includeChildren: changeType != WatcherChangeTypes.Changed),
        });
        if (pending == null || !changed)
            return;

        // A rename moves something out of one directory and into another, so both are places a
        // subscriber watching either one needs to hear about.
        var changedDirectories = UsnIndexerChangedDirectories.ForPath(normalizedPath, isDirectory);
        if (changeType == WatcherChangeTypes.Renamed && !string.IsNullOrWhiteSpace(oldPath))
            changedDirectories.AddRange(UsnIndexerChangedDirectories.ForPath(oldPath, isDirectory: false));
        PublishFolderChange(indexer, drive, changedDirectories, pending.Value);
    }

    // FolderWalkRecorder's hand-over: every path the walk saw change, checked against the disk and applied
    // inside ONE write lock, so a live monitor's event for the same path cannot land between the check and
    // the change -- whichever monitor applies last, the index ends up matching the disk. A path only ever
    // reported Changed (a folder whose LastWrite moved because a child came or went) is re-read on its own,
    // as the live monitor does. One a create/delete/rename touched is dropped with its whole subtree and
    // re-read, which also clears children it no longer has: a folder renamed away and recreated, or
    // replaced by a file of the same name. Anything under such a re-read path is covered by it and skipped.
    // ponytail: that one write lock is held across every Exists check and every structural subtree re-read, in
    // proportion to the number of distinct paths the walk recorded, plus the size of each subtree re-read. For
    // that long, searches on this drive stall, and so does every other drive's change publication and status
    // poll: they take UsnIndexer.LockObj and then read this drive's counts (UpdateTotalsFromRuntime), which
    // waits for this lock -- the same stall a large idle Compact already causes. Deliberate: checking the disk
    // outside the lock let a live event land between the check and the change. Upgrade path: stat the paths and
    // read the subtrees outside the lock, then apply under it only what no live event has touched since (a
    // per-path version in the delta to compare against), or apply in bounded batches that each re-check.
    internal static void ReconcileFolderPaths(this UsnIndexer indexer, string drive, IReadOnlyCollection<(string Path, bool Structural)> paths)
    {
        if (paths.Count == 0)
            return;

        var root = $"{drive}:\\";
        var changedDirectories = new List<string>();
        var pending = MutateFolderDrive(indexer, drive, delta =>
        {
            var states = paths.Select(p =>
            {
                var isDirectory = Directory.Exists(p.Path);
                return (p.Path, p.Structural, IsDirectory: isDirectory, Exists: isDirectory || File.Exists(p.Path));
            }).ToList();
            var rewalked = new HashSet<string>(states.Where(s => s.Structural && s.Exists).Select(s => s.Path), StringComparer.OrdinalIgnoreCase);
            foreach (var (path, structural, isDirectory, exists) in states)
            {
                if (HasAncestorIn(path, rewalked))
                    continue;
                var normalized = PathHelpers.NormalizePath(path, isDirectory);
                var changed = !exists ? DeltaPathApplier.ApplyDeleted(delta, normalized)
                    : structural ? DeltaPathApplier.ApplyRenamed(delta, (UInt128)1, root, normalized, normalized)
                    : DeltaPathApplier.ApplyCreatedOrChanged(delta, (UInt128)1, root, normalized, includeChildren: false);
                if (changed)
                    changedDirectories.AddRange(UsnIndexerChangedDirectories.ForPath(normalized, isDirectory));
            }
        });
        if (pending == null || changedDirectories.Count == 0)
            return;
        PublishFolderChange(indexer, drive, changedDirectories.Distinct(StringComparer.OrdinalIgnoreCase).ToList(), pending.Value);
    }

    private static bool HasAncestorIn(string path, HashSet<string> directories)
    {
        for (var parent = Path.GetDirectoryName(path); !string.IsNullOrEmpty(parent); parent = Path.GetDirectoryName(parent))
        {
            if (directories.Contains(parent))
                return true;
        }
        return false;
    }

    // Runs `mutate` against the drive's LiveIndex and returns the delta's pending-change count after it, or
    // null when there was no index to apply it to (flagged as missed instead if the drive is mid-rebuild).
    private static int? MutateFolderDrive(UsnIndexer indexer, string drive, Action<DeltaOverlay> mutate)
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
                return null;
            }
        }

        try
        {
            var pending = 0;
            live.Mutate((_, delta) =>
            {
                mutate(delta);
                pending = delta.PendingChangeCount;
            });
            return pending;
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
            return null;
        }
    }

    private static void PublishFolderChange(UsnIndexer indexer, string drive, List<string> changedDirectories, int pendingChangeCount)
    {
        lock (indexer.LockObj)
        {
            indexer.UpdateTotalsFromRuntime();
            indexer.UpdateDriveCounts(drive);
            // While this drive is being rebuilt its FolderDriveMonitor stays alive and keeps applying
            // changes to THIS old LiveIndex (immediately searchable); the flag lets the rebuild's caller
            // queue one follow-up refresh -- see ConsumeMissedFolderChangeDuringRebuild.
            MarkMissedIfRebuilding(indexer, drive);
        }
        indexer.RaiseDirectoriesChanged(drive, changedDirectories);
        SearchCoordinator.ClearCaches();
        // No persist here. It used to be a debounced full Compact(force: true) of the whole drive about a
        // second after any change, which on a multi-million-entry exFAT drive meant a constant rewrite
        // loop (#298). The idle compactor folds the delta in once it is big enough to be worth it
        // (UsnIndexerDurabilityExtensions.CompactIdleDeltas). Losing an unpersisted delta costs little: a
        // non-journal drive has no journal to replay, so cold start re-walks it with forceFullScan and its
        // cache only serves searches until that walk lands; DriveRecovery's non-forced rebuild reads it as
        // the walk's diff baseline only for a drive that comes back with no index loaded.
        // Only searches arm that compactor's idle tick (IdleTrimGate), so on a drive nobody searches the
        // delta would grow without bound: arm it from here too, but only past the threshold, since every
        // idle tick also forces a full GC and a working-set trim.
        // ponytail: the tick still waits for 3 s with no change, so a drive that never goes quiet that long
        // keeps growing until it does, and a long copy with pauses rewrites the whole snapshot once per
        // IdleCompactPendingThreshold changes. Upgrade path if either bites: a per-drive compaction trigger
        // that does not wait for quiet, or a threshold scaled to the drive's size.
        if (pendingChangeCount >= UsnIndexerDurabilityExtensions.IdleCompactPendingThreshold)
            indexer.DeltaCompactionDue?.Invoke();
        indexer.PublishStatusChanged();
    }

    // Caller must already hold indexer.LockObj. Flags `drive` as having missed a change during its
    // current rebuild (if State == "indexing" right now) and returns whether it did -- shared by
    // ApplyFolderChange's three "nothing to apply this change to" cases (drive not tracked at all,
    // caught a concurrent LiveIndex disposal, or applied successfully but the drive turned out to be
    // rebuilding) and by the normal in-progress path.
    private static bool MarkMissedIfRebuilding(UsnIndexer indexer, string drive)
    {
        var item = indexer.Status.Drives.FirstOrDefault(d => d.Drive.Equals(drive, StringComparison.OrdinalIgnoreCase));
        var isRebuilding = item != null && item.State == "indexing";
        if (isRebuilding)
            indexer._missedFolderChangeDuringRebuild.Add(drive);
        return isRebuilding;
    }

    // A re-walk request from a FAT32/exFAT drive's live FolderDriveMonitor: a watcher that errored or came
    // back after an outage, or a change it could not apply. While the drive's own rebuild has a
    // FolderWalkRecorder registered -- from before its walk until its hand-over -- the walk sees whatever
    // happened before it began and the recorder whatever happened since, so queuing another rebuild only
    // re-walked the whole drive again. The request is left to that recording instead, and it stays owed
    // whatever happens to the build: the hand-over covers it (a recording cut short by a cancel, a removal or
    // an overflow asks for its own one re-walk), and a build that never hands over raises it as the plain
    // request (FolderWalkRecorder.Discard, SettleFailedBuild). Otherwise the request goes through unchanged.
    internal static void RequestFolderReindex(this UsnIndexer indexer, string drive, Action<string>? onReindexRequired)
    {
        lock (indexer.LockObj)
        {
            if (indexer._walkRecorders.TryGetValue(drive, out var recorder))
            {
                recorder.DeferReindex();
                return;
            }
        }
        onReindexRequired?.Invoke(drive);
    }

    // Consumes (clears) the "a change was detected while this drive was being rebuilt" flag set by
    // ApplyFolderChange above. Called once by the rebuild's own caller (SearchEngineDriveMaintenance/
    // DriveRecovery) right after BuildDrives returns and the fresh LiveIndex is already swapped in and
    // marked ready, so a true result means a watcher change landed on this drive during the rebuild
    // window: the rebuilt snapshot predates it, and the caller has to re-walk the affected folder.
    public static bool ConsumeMissedFolderChangeDuringRebuild(this UsnIndexer indexer, string drive)
    {
        lock (indexer.LockObj)
            return indexer._missedFolderChangeDuringRebuild.Remove(drive);
    }
}
