using Lertaro.Core.Indexer.Usn;

namespace Lertaro.Core.DriveMonitoring;

// A FAT32/exFAT drive has no journal to catch up from, and its live FolderDriveMonitor only starts after
// the build (on a cold start: after EVERY drive's build). Anything created, changed or deleted behind the
// walker in that window stayed missing from search until the next restart. This watcher starts just before
// the walk and keeps what it sees until DriveMonitorFactory.EnsureMonitor has the live monitor running,
// then every path it saw change is reconciled against the disk in the freshly built index.
// ponytail: the list grows with every change made during the walk -- the same ceiling as
// FolderDriveMonitor's own _pending (IDX-01); a busy drive walked for minutes holds every event until then.
internal sealed class FolderWalkRecorder : IDisposable
{
    private readonly List<(WatcherChangeTypes Type, string Path, string? OldPath)> _changes = new();
    private volatile bool _lost;

    public FolderWalkRecorder(string drive) =>
        Monitor = new FolderDriveMonitor(drive, (type, path, oldPath) =>
        {
            lock (_changes)
                _changes.Add((type, path, oldPath));
        }, CancellationToken.None, MarkLost);

    internal FolderDriveMonitor Monitor { get; }

    public void Start() => Monitor.Start();

    public void Dispose() => Monitor.Dispose();

    // The watcher overflowed or died: what it missed only another walk can find.
    internal void MarkLost() => _lost = true;

    // The walk was cancelled -- for a removal request among other reasons -- and the watcher's handle on the
    // volume must not outlive it: it would veto "Safely remove". Whatever it would have seen is gone.
    internal void Abandon()
    {
        MarkLost();
        Dispose();
    }

    // Delivers whatever the debounce still held, then stops watching. The live monitor is already running by
    // the time this is called, so an event landing after the flush is the live monitor's to apply.
    internal (List<(WatcherChangeTypes Type, string Path, string? OldPath)> Changes, bool Lost) Stop()
    {
        Monitor.FlushPending();
        Monitor.Dispose();
        lock (_changes)
            return (_changes.ToList(), _lost);
    }

    // Records `drive` while `walk` runs, and keeps the recording for EnsureMonitor if the walk built a store.
    // ponytail: between Keep and EnsureMonitor nothing releases this watcher on a removal request -- the
    // walk's removal scope ended with the walk, and on a cold start that window lasts until every other
    // drive's walk is done too -- so "Safely remove" is vetoed until the monitors start. Upgrade path: start
    // each drive's monitor as soon as its own build ends (or give the kept recorder its own
    // DriveDeviceRemovalMonitor that calls Abandon).
    public static FileRecordStore? Record(UsnIndexer indexer, string drive, CancellationToken token, Func<FileRecordStore?> walk)
    {
        var recorder = new FolderWalkRecorder(drive);
        // Synchronously on cancel: a removal request only cancels the walk's token (DriveIndexRemovalScope,
        // CancelDriveRebuild), and this watcher holds the volume open until it is disposed.
        using var abandonOnCancel = token.Register(recorder.Abandon);
        recorder.Start();
        var kept = false;
        try
        {
            var store = walk();
            if (store != null)
            {
                Keep(indexer, drive, recorder);
                kept = true;
            }
            return store;
        }
        finally
        {
            if (!kept)
                recorder.Dispose();
        }
    }

    public static void Keep(UsnIndexer indexer, string drive, FolderWalkRecorder recorder)
    {
        FolderWalkRecorder? previous;
        lock (indexer.LockObj)
        {
            indexer._walkRecorders.Remove(drive, out previous);
            indexer._walkRecorders[drive] = recorder;
        }
        previous?.Dispose();
    }

    // For a drive whose build failed after Keep, or that turned out to have a journal after all.
    public static void Discard(UsnIndexer indexer, string drive) => Take(indexer, drive)?.Dispose();

    private static FolderWalkRecorder? Take(UsnIndexer indexer, string drive)
    {
        lock (indexer.LockObj)
            return indexer._walkRecorders.Remove(drive, out var recorder) ? recorder : null;
    }

    public static void Replay(UsnIndexer indexer, string drive, Action<string>? onReindexRequired)
    {
        var recorder = Take(indexer, drive);
        if (recorder == null)
            return;

        // Reconciled, not replayed: the live monitor is already running and may have applied a newer event for
        // the same path, so replaying a recorded Deleted(x) after its live Created(x) dropped a file that
        // exists. Each path the walk saw change (a rename's old and new path alike) is checked against the
        // disk instead -- see UsnIndexerExtensions.ReconcileFolderPaths. Keyed case-insensitively like the
        // file system, keeping the LATEST spelling so a case-only rename ends up with the new name.
        var (changes, lost) = recorder.Stop();
        var paths = new Dictionary<string, (string Path, bool Structural)>(StringComparer.OrdinalIgnoreCase);
        void Note(string path, bool structural) =>
            paths[path] = (path, structural || paths.TryGetValue(path, out var seen) && seen.Structural);
        foreach (var (type, path, oldPath) in changes)
        {
            if (oldPath != null)
                Note(oldPath, structural: true);
            Note(path, structural: type != WatcherChangeTypes.Changed);
        }

        try
        {
            indexer.ReconcileFolderPaths(drive, paths.Values);
        }
        catch (Exception ex)
        {
            // Runs inside EnsureMonitor, and on a cold start an exception here would stop every later
            // drive's monitor from starting. A re-walk finds whatever this reconcile could not apply.
            Logger.Log($"[FolderWalkRecorder] Drive {drive}: reconciling the changes seen during its walk failed; requesting a re-walk. {ex.Message}", LogLevel.Error);
            lost = true;
        }
        // The recorder saw every change since the walk began, so a change the old monitor flagged as missed
        // during this rebuild is already reconciled above: without this, the caller's follow-up rebuild
        // (ConsumeMissedFolderChangeDuringRebuild) re-walked the whole drive, and on a drive written to all
        // the time that walk saw a change too and asked for the next one. A recorder that lost events still
        // asks for exactly one walk, below.
        indexer.ConsumeMissedFolderChangeDuringRebuild(drive);
        if (lost)
            onReindexRequired?.Invoke(drive);
    }
}
