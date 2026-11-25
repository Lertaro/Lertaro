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
    // The drive's live monitor asked for a re-walk while this recording covered it (see
    // UsnIndexerExtensions.RequestFolderReindex). Guarded by UsnIndexer.LockObj.
    private bool _reindexDeferred;
    // The owning build's token registration (see the constructor). Released with the recorder, so a token
    // that outlives it does not keep it, and every change it holds, reachable.
    private IDisposable? _abandonOnCancel;

    // `token` is the build's own: the per-drive rebuild's, or the startup initialization's -- NOT a walk's
    // DriveIndexRemovalScope token, whose source is disposed (and so unlinked from the build's) as soon as
    // that drive's walk ends. It identifies the build (see Take) and releases the recorder when cancelled.
    public FolderWalkRecorder(string drive, CancellationToken token = default)
    {
        Owner = token;
        Monitor = new FolderDriveMonitor(drive, (type, path, oldPath) =>
        {
            lock (_changes)
                _changes.Add((type, path, oldPath));
        }, CancellationToken.None, MarkLost, onUnavailable: MarkLost);
        // Its own answer to "Safely remove", for as long as it lives. The walk's removal scope ends with the
        // walk, and on a cold start the live monitor's listener only attaches in EnsureMonitor, after EVERY
        // drive's walk: until then this watcher's handle would veto the eject. Releasing it is all a request
        // does: the recording is then lost, so a hand-over that finds the volume still (or again) mounted asks
        // for one re-walk, whether the removal failed or completed.
        Removal = new DriveDeviceRemovalMonitor(drive, _ => Abandon(), _ => { }, _ => { });
        // A volume that cannot be opened for notifications is logged there, and this listener never fires.
        Removal.RegisterCore(drive, null);
        // Synchronously on cancel: a Stop, a full rebuild or shutdown, or a removal request the build's owner
        // hears (CancelDriveRebuild) arrives only as this token's cancel, and this watcher holds the volume
        // open until it is disposed -- during the walk and after it, until the hand-over. A token cancelled
        // already runs Abandon inside Register, before the registration is stored here; the stored one is
        // then spent, and disposing it does nothing.
        _abandonOnCancel = token.Register(Abandon);
    }

    internal CancellationToken Owner { get; }

    internal FolderDriveMonitor Monitor { get; }

    internal DriveDeviceRemovalMonitor Removal { get; }

    public void Start() => Monitor.Start();

    public void Dispose()
    {
        // Also runs inside the cancel registration's own callback (Abandon), which is safe: a registration
        // disposed on the thread running its callback does not wait for that callback.
        Interlocked.Exchange(ref _abandonOnCancel, null)?.Dispose();
        // Never waits for a CM callback: this can run inside one, its own or the live monitor's (Abandon via
        // CancelDriveRebuild, under UsnIndexer.LockObj). The listener's volume handle closes right here.
        Removal.DisposeWithoutWaiting();
        Monitor.Dispose();
    }

    // The watcher overflowed, died, or was not up at some point: what it missed only another walk can find.
    internal void MarkLost() => _lost = true;

    // The walk was cancelled, or the volume is being removed, and the watcher's handle on it must not outlive
    // that: it would veto "Safely remove". Whatever it would have seen is gone. Can run under
    // UsnIndexer.LockObj (a live monitor's removal callback cancelling the rebuild): Monitor.Dispose then
    // waits only for a flush delivering into this recorder's own list, which takes no other lock.
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
        Dispose();
        lock (_changes)
            return (_changes.ToList(), _lost);
    }

    // Caller holds UsnIndexer.LockObj.
    internal void DeferReindex() => _reindexDeferred = true;

    // Records `drive` while `walk` runs, for EnsureMonitor's hand-over. `token` is the build's own (see the
    // constructor); `walk` uses whatever token it needs. Registered for the drive before the walk starts,
    // not after it: while it is, the drive's live monitor leaves its re-walk requests to this recording
    // (UsnIndexerExtensions.RequestFolderReindex). A walk that builds nothing stops watching but stays
    // registered, so the build's settle (SettleFailedBuild, or the startup's own) finds what was left to it.
    public static FileRecordStore? Record(UsnIndexer indexer, string drive, CancellationToken token, Func<FileRecordStore?> walk)
    {
        var recorder = new FolderWalkRecorder(drive, token);
        Keep(indexer, drive, recorder);
        recorder.Start();
        var built = false;
        try
        {
            var store = walk();
            built = store != null;
            return store;
        }
        finally
        {
            if (!built)
                recorder.Dispose();
        }
    }

    // A build whose token is already cancelled registers nothing: it is a stale build still unwinding (a full
    // rebuild cancels every per-drive rebuild before it starts its own), its recorder was abandoned the moment
    // it was created, and the recording registered for the drive now may be the live build's. A recorder that
    // is still registered when another registers belongs to an older build that has not handed over yet; it
    // stops watching now, and that build asks for the re-walk at its hand-over, which finds the recording is
    // not its own (Replay). Decided under the lock RequestFolderReindex and Take decide under.
    public static void Keep(UsnIndexer indexer, string drive, FolderWalkRecorder recorder)
    {
        FolderWalkRecorder? previous;
        lock (indexer.LockObj)
        {
            if (recorder.Owner.IsCancellationRequested)
                return;
            indexer._walkRecorders.Remove(drive, out previous);
            indexer._walkRecorders[drive] = recorder;
        }
        previous?.Abandon();
    }

    // For a drive whose build (identified by `owner`, its token) failed, or that turned out to have a journal
    // after all. Returns whether the drive's live monitor left a re-walk to the discarded recording, which the
    // caller now owes. Taken under the lock RequestFolderReindex decides under, so nothing is left to the
    // recording after this.
    public static bool Discard(UsnIndexer indexer, string drive, CancellationToken owner)
    {
        FolderWalkRecorder? recorder;
        bool owed;
        lock (indexer.LockObj)
        {
            recorder = Take(indexer, drive, owner);
            owed = recorder?._reindexDeferred == true;
        }
        recorder?.Dispose();
        return owed;
    }

    // Only the recorder `owner`'s own build kept: a newer build's, still walking, is not this build's to stop.
    private static FolderWalkRecorder? Take(UsnIndexer indexer, string drive, CancellationToken owner)
    {
        lock (indexer.LockObj)
        {
            if (!indexer._walkRecorders.TryGetValue(drive, out var recorder) || recorder.Owner != owner)
                return null;
            indexer._walkRecorders.Remove(drive);
            return recorder;
        }
    }

    // The hand-over of the walk `owner`'s build just swapped in. Only ever called after a walk of `drive`.
    public static void Replay(UsnIndexer indexer, string drive, CancellationToken owner, Action<string>? onReindexRequired)
    {
        var recorder = Take(indexer, drive, owner);
        if (recorder == null)
        {
            // This build's recording is not here: another build's replaced it (Keep), whether or not that one
            // has handed over or been discarded since, or this build registered none because it was already
            // cancelled. Either way nothing recorded this drive for the whole of this build's walk, so only a
            // walk recovers what changed meanwhile.
            Logger.Log($"[FolderWalkRecorder] Drive {drive}: this build's walk recording is not there to hand over; requesting a re-walk.", LogLevel.Warn);
            onReindexRequired?.Invoke(drive);
            return;
        }

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
        // during this rebuild is already reconciled above, and so is whatever made it ask for a re-walk
        // meanwhile (RequestFolderReindex left that to this recording). Without this, the caller's follow-up
        // rebuild (ConsumeMissedFolderChangeDuringRebuild) re-walked the whole drive, and on a drive written
        // to all the time that walk saw a change too and asked for the next one. A recorder that lost events
        // still asks for exactly one walk, below.
        indexer.ConsumeMissedFolderChangeDuringRebuild(drive);
        if (lost)
            onReindexRequired?.Invoke(drive);
    }
}
