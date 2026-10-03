using Lertaro.Core.Indexer.NetworkDrive.Walk;

namespace Lertaro.Core.Indexer.Usn.Journal;

// Replaces FolderDriveScanner: builds a non-journal (FAT32/exFAT/etc.) local drive's FileRecordStore by
// calling straight into the same TreeBuilder/TreeDiffBaseline/checkpoint machinery network/WSL/folder-index
// drives use (Core/Indexer/NetworkDrive/Walk/*), instead of a hand-rolled single-threaded recursive walk
// with no incremental reuse and no checkpoint/resume. That machinery is already fully generic local-
// filesystem code with no network/UNC/WSL coupling in the traversal itself -- calling it directly from this
// (elevated-service-process) namespace needs no porting, just an orchestrator modeled on NetworkIndex.Build.
//
// The exclusion/ignore rules are not read here: they arrive as `options` from the caller, because this
// walk runs in the --service process, where UserSettings resolves to a per-user directory that does not
// exist for LocalSystem -- reading it there silently produced defaults, so the user's exclusions never
// reached the walk at all. The production caller passes WalkOptions.FromMachineSettings, the source the
// service can actually read.
internal static class LocalDriveWalkBuilder
{
    // `root` is taken as an explicit parameter (not derived from `drive` internally) so this can be pointed
    // at a real temp directory in tests, mirroring NetworkIndex.Build's own decoupled drive/root/physicalRoot
    // parameters -- the production caller just passes "{drive}:\\" for both `root` and `drive`'s volume-
    // identity lookup, same effective behavior FolderDriveScanner had. `options` is required rather than
    // defaulted for the same reason it exists: a default would have to mean "walk unfiltered", which is
    // precisely the silent no-op that made the user's exclusions unreachable. A caller that genuinely
    // wants no filtering says so with WalkOptions.From([], [], []).
    public static FileRecordStore Build(
        string drive,
        string root,
        FileRecordStore? previousStore,
        Action<int, int> onProgress,
        CancellationToken token,
        WalkOptions options,
        bool forceFullScan = false,
        Action<FileRecordStore, NetworkDriveWalkStats>? onCheckpoint = null)
    {
        const ulong rootId = 1;
        var identity = VolumeHelper.GetVolumeIdentity(drive);
        var store = new FileRecordStore
        {
            SourceKey = drive,
            SourceKind = FileRecordSourceKind.LocalMft,
            IdKind = FileRecordIdKind.SourceLocalId64,
            FileSystemType = identity?.FileSystemType ?? string.Empty,
            VolumeSerialNumber = identity?.SerialNumber ?? 0,
            RootId = rootId,
        };

        var rootLastWriteTime = FileTimeHelper.TryGetLastWriteTimeUnixSeconds(root);

        store.Records.Add(new FileRecord(
            rootId,
            rootId,
            string.Empty,
            FileRecordFlags.Directory | FileRecordFlags.SourceRoot,
            lastWriteTimeUnixSeconds: rootLastWriteTime));

        var diffBaseline = forceFullScan ? null : TreeDiffBaseline.From(previousStore);
        // recheckExclusions is unconditionally true here, unlike NetworkIndex.Build's fingerprint comparison:
        // a local MFT store never carries ExclusionRulesFingerprint (see FileRecordStore's own comment on
        // it), so that comparison would always be "" != "" and never recheck. Unconditional is also the
        // correct answer for this path -- the caller's rules can change between passes (the App mirrors a
        // new ExcludedPaths into machine-settings.json) with no signal reaching this walk.
        var builder = new TreeBuilder(store, root, root, options, token, onProgress, onCheckpoint, diffBaseline, recheckExclusions: true);
        var stats = builder.Run();

        // Reaching here without cancellation only means TreeBuilder.Run() drained its queue -- NOT that
        // every directory's real contents were captured; a directory that failed to enumerate stays
        // un-Listed for a future rebuild to retry regardless (see TreeBuilder.WalkDirectory). Marking
        // complete anyway mirrors NetworkIndex's own reasoning -- see DriveRefreshRunner.RefreshDrive's own
        // comment on why gating this on Errors == 0 would make it functionally never become true.
        store.IsComplete = true;
        if (stats.Errors > 0)
            Logger.Log($"[LocalDriveWalkBuilder] {drive}: finished with {stats.Errors} error(s) ({stats.EnumerateErrors} enumerate, {stats.AttributeErrors} attribute) -- marking complete anyway; affected directories stay un-Listed for a future manual rebuild to retry.", LogLevel.Warn);
        return store;
    }
}
