using Lertaro.Core.Indexer.NetworkDrive.Walk;

namespace Lertaro.Core.Indexer.Usn.Journal;

// Replaces FolderDriveScanner: builds a non-journal (FAT32/exFAT/etc.) local drive's FileRecordStore by
// calling straight into the same TreeBuilder/TreeDiffBaseline/checkpoint machinery network/WSL/folder-index
// drives use (Core/Indexer/NetworkDrive/Walk/*), instead of a hand-rolled single-threaded recursive walk
// with no incremental reuse and no checkpoint/resume. That machinery is already fully generic local-
// filesystem code with no network/UNC/WSL coupling in the traversal itself -- calling it directly from this
// (elevated-service-process) namespace needs no porting, just an orchestrator modeled on NetworkIndex.Build.
//
// Deliberately does NOT apply exclusion/ignore rules (WalkOptions is always empty/no-op below) -- local
// drives never have, and this refactor isn't the place to introduce that as a new behavior change.
internal static class LocalDriveWalkBuilder
{
    private static readonly WalkOptions NoFiltering = new(
        ExcludedPaths: Array.Empty<string>(),
        IgnoredPathGlobs: Array.Empty<string>(),
        IgnoredPathRegexes: Array.Empty<string>(),
        MaxDepth: 0,
        WorkerCount: 0,
        UseIgnoreFiles: false);

    // `root` is taken as an explicit parameter (not derived from `drive` internally) so this can be pointed
    // at a real temp directory in tests, mirroring NetworkIndex.Build's own decoupled drive/root/physicalRoot
    // parameters -- the production caller just passes "{drive}:\\" for both `root` and `drive`'s volume-
    // identity lookup, same effective behavior FolderDriveScanner had.
    public static FileRecordStore Build(
        string drive,
        string root,
        FileRecordStore? previousStore,
        Action<int, int> onProgress,
        CancellationToken token,
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

        var diffBaseline = TreeDiffBaseline.From(previousStore);
        var builder = new TreeBuilder(store, root, root, NoFiltering, token, onProgress, onCheckpoint, diffBaseline, recheckExclusions: false);
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
