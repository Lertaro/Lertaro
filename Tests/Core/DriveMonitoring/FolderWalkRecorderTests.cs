using Lertaro.Core.DriveMonitoring;
using Lertaro.Core.IndexV2;
using Lertaro.Core.IndexV2.Delta;
using Lertaro.Core.Indexer.NetworkDrive.Walk;
using Lertaro.Core.Indexer.Usn;
using Lertaro.Core.Tests.IndexV2;

namespace Lertaro.Core.Tests.DriveMonitoring;

// The recorder's watcher is real OS plumbing (FileSystemWatcher inside DriveWatcherHost), so these drive its
// FolderDriveMonitor through Schedule -- the same entry point the watcher's events use -- without starting it.
[TestClass]
public sealed class FolderWalkRecorderTests
{
    // The hand-over race: the live monitor is already up, so whatever the recorder's 250 ms debounce still
    // holds must come back from Stop rather than be dropped with the watcher.
    [TestMethod]
    public void Stop_ReturnsChangesTheDebounceHadNotDeliveredYet_AndIgnoresLaterOnes()
    {
        using var recorder = new FolderWalkRecorder("Q");
        recorder.Monitor.Schedule(WatcherChangeTypes.Created, @"Q:\new.txt");
        recorder.Monitor.Schedule(WatcherChangeTypes.Renamed, @"Q:\b", @"Q:\a");

        var (changes, lost) = recorder.Stop();
        recorder.Monitor.Schedule(WatcherChangeTypes.Deleted, @"Q:\late.txt");
        recorder.Monitor.FlushPending();

        CollectionAssert.AreEqual(new[]
        {
            (WatcherChangeTypes.Created, @"Q:\new.txt", (string?)null),
            (WatcherChangeTypes.Renamed, @"Q:\b", @"Q:\a"),
        }, changes);
        Assert.IsFalse(lost);
        Assert.HasCount(2, recorder.Stop().Changes);
    }

    // The gap this exists for: a file created behind the walker reaches the rebuilt index once the live
    // monitor takes over, without waiting for the next restart.
    [TestMethod]
    public void Replay_AppliesWhatChangedDuringTheWalkToTheBuiltIndex()
    {
        using var tempDir = new TempDirectory();
        var filePath = Path.Combine(tempDir.Path, "made-during-walk.txt");
        File.WriteAllText(filePath, "x");
        using var fixture = LiveIndexFixture.Build("C", new[] { LiveIndexFixture.Root() });
        var indexer = new UsnIndexer();
        indexer._recordIndexes["C"] = fixture.Index;
        indexer.Status.Drives.Add(new UsnIndexer.DriveIndexStatus { Drive = "C", State = "ready" });
        var recorder = new FolderWalkRecorder("C");
        recorder.Monitor.Schedule(WatcherChangeTypes.Created, filePath);
        FolderWalkRecorder.Keep(indexer, "C", recorder);
        var reindexRequests = 0;

        FolderWalkRecorder.Replay(indexer, "C", CancellationToken.None, _ => reindexRequests++);

        Assert.AreEqual(1, fixture.Index.GetCounts().Files);
        Assert.AreEqual(0, reindexRequests);
        Assert.IsFalse(indexer.ConsumeMissedFolderChangeDuringRebuild("C"), "a replay must not queue another walk");

        // The fixture owns (and disposes) its index; a second Dispose of it would throw.
        indexer._recordIndexes.Clear();
        indexer.Dispose();
    }

    // A watcher that overflowed mid-walk dropped events nobody can list; only another walk recovers them.
    [TestMethod]
    public void Replay_RecorderThatLostEvents_RequestsAReindex()
    {
        using var indexer = new UsnIndexer();
        var recorder = new FolderWalkRecorder("C");
        recorder.MarkLost();
        FolderWalkRecorder.Keep(indexer, "C", recorder);
        var requested = new List<string>();

        FolderWalkRecorder.Replay(indexer, "C", CancellationToken.None, requested.Add);

        CollectionAssert.AreEqual(new[] { "C" }, requested);
    }

    // Review on #561: the live monitor starts before the hand-over, so it can apply Created(x) for a file the
    // walk saw deleted and that came back meanwhile. Replaying the recorded Deleted(x) after that dropped a
    // file that exists, and nothing put it back until x changed again.
    [TestMethod]
    public void Replay_RecordedDeleteOfAFileRecreatedDuringTheHandOver_KeepsTheFile()
    {
        using var tempDir = new TempDirectory();
        var file = Path.Combine(tempDir.Path, "x.txt");
        var (indexer, fixture) = CreateIndexer();
        var recorder = new FolderWalkRecorder("C");
        recorder.Monitor.Schedule(WatcherChangeTypes.Deleted, file);
        FolderWalkRecorder.Keep(indexer, "C", recorder);
        File.WriteAllText(file, "back");
        indexer.ApplyFolderChange("C", WatcherChangeTypes.Created, file); // the live monitor's newer event

        FolderWalkRecorder.Replay(indexer, "C", CancellationToken.None, null);

        Assert.IsTrue(InIndex(fixture.Index, file, isDirectory: false));
        Dispose(indexer, fixture);
    }

    // The mirror case: a file the walk saw created and that is gone again must not be resurrected.
    [TestMethod]
    public void Replay_RecordedCreateOfAFileDeletedDuringTheHandOver_LeavesItOut()
    {
        using var tempDir = new TempDirectory();
        var file = Path.Combine(tempDir.Path, "x.txt");
        var (indexer, fixture) = CreateIndexer();
        var recorder = new FolderWalkRecorder("C");
        recorder.Monitor.Schedule(WatcherChangeTypes.Created, file);
        FolderWalkRecorder.Keep(indexer, "C", recorder);

        FolderWalkRecorder.Replay(indexer, "C", CancellationToken.None, null);

        Assert.IsFalse(InIndex(fixture.Index, file, isDirectory: false));
        Dispose(indexer, fixture);
    }

    // A folder renamed while the walk ran: the old path leaves the index, the new one arrives with its files.
    [TestMethod]
    public void Replay_FolderRenamedDuringTheWalk_MovesItsSubtree()
    {
        using var tempDir = new TempDirectory();
        var oldDir = Path.Combine(tempDir.Path, "a");
        var newDir = Path.Combine(tempDir.Path, "b");
        Directory.CreateDirectory(oldDir);
        File.WriteAllText(Path.Combine(oldDir, "f.txt"), "x");
        var (indexer, fixture) = CreateIndexer();
        indexer.ApplyFolderChange("C", WatcherChangeTypes.Created, oldDir); // what the walk captured
        Directory.Move(oldDir, newDir);
        var recorder = new FolderWalkRecorder("C");
        recorder.Monitor.Schedule(WatcherChangeTypes.Renamed, newDir, oldDir);
        FolderWalkRecorder.Keep(indexer, "C", recorder);

        FolderWalkRecorder.Replay(indexer, "C", CancellationToken.None, null);

        Assert.IsFalse(InIndex(fixture.Index, oldDir, isDirectory: true));
        Assert.IsTrue(InIndex(fixture.Index, newDir, isDirectory: true));
        Assert.IsTrue(InIndex(fixture.Index, Path.Combine(newDir, "f.txt"), isDirectory: false));
        Dispose(indexer, fixture);
    }

    // A folder that only reported Changed (raised on it when a child is added) is re-read alone, as the live
    // monitor does: re-reading its subtree turned one new file into a walk of everything beneath it.
    [TestMethod]
    public void Replay_FolderThatOnlyReportedChanged_IsNotReReadWithItsSubtree()
    {
        using var tempDir = new TempDirectory();
        var big = Path.Combine(tempDir.Path, "big");
        Directory.CreateDirectory(big);
        for (var i = 0; i < 500; i++)
            File.WriteAllText(Path.Combine(big, $"f{i}.txt"), "");
        var (indexer, fixture) = CreateIndexer();
        indexer.ApplyFolderChange("C", WatcherChangeTypes.Created, big); // what the walk captured...
        fixture.Index.Compact(fixture.Path, force: true);                // ...as the built base snapshot
        var added = Path.Combine(big, "new.txt");
        File.WriteAllText(added, "");
        var recorder = new FolderWalkRecorder("C");
        recorder.Monitor.Schedule(WatcherChangeTypes.Created, added);
        recorder.Monitor.Schedule(WatcherChangeTypes.Changed, big);
        FolderWalkRecorder.Keep(indexer, "C", recorder);

        FolderWalkRecorder.Replay(indexer, "C", CancellationToken.None, null);

        var pending = fixture.Index.PendingChangeCount;
        Assert.IsTrue(InIndex(fixture.Index, added, isDirectory: false));
        Dispose(indexer, fixture);
        Assert.IsTrue(pending <= 10, $"one new file in a 500-entry folder left {pending} pending changes");
    }

    // A folder renamed away during the walk and recreated empty: the walk's children under the old name are
    // gone from disk and must go from the index, even though the path itself still exists.
    [TestMethod]
    public void Replay_FolderRenamedAwayAndRecreated_DropsItsOldChildren()
    {
        using var tempDir = new TempDirectory();
        var p = Path.Combine(tempDir.Path, "P");
        var pOld = Path.Combine(tempDir.Path, "P-old");
        Directory.CreateDirectory(p);
        File.WriteAllText(Path.Combine(p, "a.txt"), "x");
        var (indexer, fixture) = CreateIndexer();
        indexer.ApplyFolderChange("C", WatcherChangeTypes.Created, p);
        fixture.Index.Compact(fixture.Path, force: true);
        Directory.Move(p, pOld);
        Directory.CreateDirectory(p);
        var recorder = new FolderWalkRecorder("C");
        recorder.Monitor.Schedule(WatcherChangeTypes.Renamed, pOld, p);
        recorder.Monitor.Schedule(WatcherChangeTypes.Created, p);
        FolderWalkRecorder.Keep(indexer, "C", recorder);

        FolderWalkRecorder.Replay(indexer, "C", CancellationToken.None, null);

        Assert.IsTrue(InIndex(fixture.Index, Path.Combine(pOld, "a.txt"), isDirectory: false));
        Assert.IsFalse(InIndex(fixture.Index, Path.Combine(p, "a.txt"), isDirectory: false));
        Dispose(indexer, fixture);
    }

    // A folder moved away (to the Recycle Bin: one Renamed, no child events) and a FILE of the same name
    // created in its place: the folder's old children must go.
    [TestMethod]
    public void Replay_FolderReplacedByAFileOfTheSameName_DropsItsOldChildren()
    {
        using var tempDir = new TempDirectory();
        var a = Path.Combine(tempDir.Path, "a");
        var away = Path.Combine(tempDir.Path, "recycled");
        Directory.CreateDirectory(a);
        File.WriteAllText(Path.Combine(a, "f.txt"), "x");
        var (indexer, fixture) = CreateIndexer();
        indexer.ApplyFolderChange("C", WatcherChangeTypes.Created, a);
        fixture.Index.Compact(fixture.Path, force: true);
        Directory.Move(a, away);
        File.WriteAllText(a, "now a file");
        var recorder = new FolderWalkRecorder("C");
        recorder.Monitor.Schedule(WatcherChangeTypes.Renamed, away, a);
        recorder.Monitor.Schedule(WatcherChangeTypes.Created, a);
        FolderWalkRecorder.Keep(indexer, "C", recorder);

        FolderWalkRecorder.Replay(indexer, "C", CancellationToken.None, null);

        Assert.IsTrue(InIndex(fixture.Index, a, isDirectory: false));
        Assert.IsFalse(InIndex(fixture.Index, Path.Combine(a, "f.txt"), isDirectory: false));
        Dispose(indexer, fixture);
    }

    // The disk check must happen under the LiveIndex write lock: checked before it, a live Created(x) applied
    // while Replay waited for that lock was undone by Replay's stale "x is gone".
    [TestMethod]
    public void Replay_LiveCreatedAppliedWhileReplayWaitsForTheLock_IsNotUndone()
    {
        using var tempDir = new TempDirectory();
        var x = Path.Combine(tempDir.Path, "x.txt");
        var (indexer, fixture) = CreateIndexer();
        var recorder = new FolderWalkRecorder("C");
        recorder.Monitor.Schedule(WatcherChangeTypes.Deleted, x); // the walk saw x go
        FolderWalkRecorder.Keep(indexer, "C", recorder);
        // Replay runs on a thread of its own, so its state says when it blocks. Nothing it does before taking
        // the LiveIndex write lock waits on anything contended, so once it waits, it waits for that lock.
        var replay = new Thread(() => FolderWalkRecorder.Replay(indexer, "C", CancellationToken.None, null)) { IsBackground = true };
        var replayBlocked = false;

        using var held = new ManualResetEventSlim();
        var holder = Task.Run(() => fixture.Index.Mutate((_, delta) =>
        {
            held.Set();
            // Once Replay is queued for this write lock, x comes back and the live monitor's Created(x) gets the
            // lock first.
            replayBlocked = SpinWait.SpinUntil(() => (replay.ThreadState & ThreadState.WaitSleepJoin) != 0, TimeSpan.FromSeconds(30));
            File.WriteAllText(x, "back");
            DeltaPathApplier.ApplyCreatedOrChanged(delta, 1, @"C:\", x);
        }));
        Assert.IsTrue(held.Wait(TimeSpan.FromSeconds(30)), "holder never took the lock");
        replay.Start();
        Assert.IsTrue(holder.Wait(TimeSpan.FromSeconds(30)) && replay.Join(TimeSpan.FromSeconds(30)), "did not finish");

        Assert.IsTrue(replayBlocked, "precondition: Replay never blocked on the write lock");
        Assert.IsTrue(InIndex(fixture.Index, x, isDirectory: false));
        Dispose(indexer, fixture);
    }

    // A case-only rename during the walk (foo.txt -> Foo.txt): the path set is case-insensitive, and keeping
    // the first spelling it saw left the index saying foo.txt.
    [TestMethod]
    public void Replay_CaseOnlyRenameDuringTheWalk_IndexesTheNewSpelling()
    {
        using var tempDir = new TempDirectory();
        var lower = Path.Combine(tempDir.Path, "foo.txt");
        var upper = Path.Combine(tempDir.Path, "Foo.txt");
        File.WriteAllText(lower, "x");
        var (indexer, fixture) = CreateIndexer();
        indexer.ApplyFolderChange("C", WatcherChangeTypes.Created, lower);
        fixture.Index.Compact(fixture.Path, force: true);
        File.Move(lower, upper);
        var recorder = new FolderWalkRecorder("C");
        recorder.Monitor.Schedule(WatcherChangeTypes.Renamed, upper, lower);
        FolderWalkRecorder.Keep(indexer, "C", recorder);

        FolderWalkRecorder.Replay(indexer, "C", CancellationToken.None, null);

        var id = (UInt128)PathHelpers.HashPath64(PathHelpers.NormalizePath(upper, false));
        var names = fixture.Index.ToStore().Records.Where(r => r.Id == id).Select(r => r.Name).ToArray();
        Dispose(indexer, fixture);
        CollectionAssert.AreEqual(new[] { "Foo.txt" }, names);
    }

    // Replay runs inside EnsureMonitor, where an exception stops the drive's hand-over and, on a cold start,
    // every later drive's monitor. Here the LiveIndex refuses the write lock (a read lock is held on this
    // thread); in the field it is an IOException/UnauthorizedAccessException from the disk work.
    [TestMethod]
    public void Replay_ReconcileThrows_RequestsAReWalkInsteadOfThrowing()
    {
        using var tempDir = new TempDirectory();
        var (indexer, fixture) = CreateIndexer();
        var recorder = new FolderWalkRecorder("C");
        recorder.Monitor.Schedule(WatcherChangeTypes.Created, Path.Combine(tempDir.Path, "x.txt"));
        FolderWalkRecorder.Keep(indexer, "C", recorder);
        var requested = new List<string>();

        fixture.Index.Read((_, _) =>
        {
            FolderWalkRecorder.Replay(indexer, "C", CancellationToken.None, requested.Add);
            return 0;
        });

        Dispose(indexer, fixture);
        CollectionAssert.AreEqual(new[] { "C" }, requested);
    }

    // Past the compaction threshold the hand-over arms the idle compactor too, like ApplyFolderChange.
    [TestMethod]
    public void Replay_LeavesTheDeltaPastTheCompactionThreshold_SignalsCompactionDue()
    {
        using var tempDir = new TempDirectory();
        var file = Path.Combine(tempDir.Path, "x.txt");
        File.WriteAllText(file, "x");
        var (indexer, fixture) = CreateIndexer();
        fixture.Index.Mutate((_, delta) =>
        {
            for (var i = 0; i < UsnIndexerDurabilityExtensions.IdleCompactPendingThreshold; i++)
                delta.Upsert((UInt128)(1_000_000 + i), 1, $"f{i}", FileRecordFlags.None, 0, 0, 0, 0);
        });
        var signals = 0;
        indexer.DeltaCompactionDue = () => signals++;
        var recorder = new FolderWalkRecorder("C");
        recorder.Monitor.Schedule(WatcherChangeTypes.Created, file);
        FolderWalkRecorder.Keep(indexer, "C", recorder);

        FolderWalkRecorder.Replay(indexer, "C", CancellationToken.None, null);

        Dispose(indexer, fixture);
        Assert.AreEqual(1, signals);
    }

    // A build that fails after its walk kept a recorder (OnDriveCompleted's snapshot write throwing, say)
    // settles through SettleFailedBuild, from every per-drive path; nothing would take the recorder over.
    [TestMethod]
    public void SettleFailedBuild_RecorderKeptByTheWalk_IsReleased()
    {
        using var indexer = new UsnIndexer();
        indexer.Status.Drives.Add(new UsnIndexer.DriveIndexStatus { Drive = "C", State = "indexing" });
        var recorder = new FolderWalkRecorder("C");
        FolderWalkRecorder.Keep(indexer, "C", recorder);

        DriveArrivalRecovery.SettleFailedBuild(indexer, "C", wasCancelled: false, CancellationToken.None, CancellationToken.None, null, null);

        recorder.Monitor.Schedule(WatcherChangeTypes.Created, @"C:\after-failure.txt");
        bool stillKept;
        lock (indexer.LockObj)
            stillKept = indexer._walkRecorders.ContainsKey("C");
        Assert.IsFalse(stillKept);
        Assert.HasCount(0, recorder.Stop().Changes, "still recording");
    }

    // EnsureMonitor takes the journal branch for a drive the walk treated as FAT/exFAT when the volume cannot
    // be queried any more (GetFileSystemType falls back to NTFS) -- "Q" is unmounted here.
    [TestMethod]
    public void EnsureMonitor_JournalBranch_ReleasesTheWalkRecorder()
    {
        Assert.IsFalse(Directory.Exists(@"Q:\"), "precondition: Q: must not be mounted");
        var indexer = new UsnIndexer();
        FolderWalkRecorder.Keep(indexer, "Q", new FolderWalkRecorder("Q"));
        using var cts = new CancellationTokenSource();

        DriveMonitorFactory.EnsureMonitor(indexer, "Q", 0, 0, cts.Token, CancellationToken.None, null);

        bool stillKept;
        lock (indexer.LockObj)
            stillKept = indexer._walkRecorders.ContainsKey("Q");
        cts.Cancel();
        indexer.Dispose();
        Assert.IsFalse(stillKept);
    }

    // A removal request only cancels the walk's token, and the recorder's watcher holds the volume open:
    // cancelling must dispose it there and then, or "Safely remove" is vetoed. What it missed needs a re-walk.
    [TestMethod]
    public void Record_WalkTokenCancelled_ReleasesTheWatcherAndMarksItLost()
    {
        using var indexer = new UsnIndexer();
        using var cts = new CancellationTokenSource();

        // The walk still returns a store, so the recorder is kept and its state can be inspected.
        FolderWalkRecorder.Record(indexer, "Q", cts.Token, () =>
        {
            cts.Cancel();
            return new FileRecordStore();
        });
        FolderWalkRecorder recorder;
        lock (indexer.LockObj)
            recorder = indexer._walkRecorders["Q"];
        recorder.Monitor.Schedule(WatcherChangeTypes.Created, @"Q:\after-cancel.txt");
        var requested = new List<string>();
        var (changes, _) = recorder.Stop();
        FolderWalkRecorder.Replay(indexer, "Q", cts.Token, requested.Add);

        Assert.HasCount(0, changes, "still recording after the cancel");
        CollectionAssert.AreEqual(new[] { "Q" }, requested);
    }

    // A busy drive's rebuild: the old monitor flags each change made during it as missed, and the caller
    // re-walked the whole drive for it -- a walk that saw a change of its own and asked for the next one.
    // The recorder already holds that change, so its hand-over must leave nothing to re-walk for.
    [TestMethod]
    public void Replay_ChangeFlaggedAsMissedDuringTheRebuild_LeavesNoFollowUpWalk()
    {
        using var tempDir = new TempDirectory();
        var file = Path.Combine(tempDir.Path, "written-during-rebuild.txt");
        File.WriteAllText(file, "x");
        var (indexer, fixture) = CreateIndexer();
        var status = indexer.Status.Drives.Single(d => d.Drive == "C");
        status.State = "indexing";
        indexer.ApplyFolderChange("C", WatcherChangeTypes.Created, file); // the old monitor, mid-rebuild
        var recorder = new FolderWalkRecorder("C");
        recorder.Monitor.Schedule(WatcherChangeTypes.Created, file);
        FolderWalkRecorder.Keep(indexer, "C", recorder);
        status.State = "ready";

        FolderWalkRecorder.Replay(indexer, "C", CancellationToken.None, null);

        Assert.IsFalse(indexer.ConsumeMissedFolderChangeDuringRebuild("C"));
        Assert.IsTrue(InIndex(fixture.Index, file, isDirectory: false));
        Dispose(indexer, fixture);
    }

    // No recorder means nothing saw the rebuild's changes (a journal drive's dropped records set the same
    // flag), so the follow-up walk must still be asked for.
    [TestMethod]
    public void Replay_WithoutARecorder_KeepsTheMissedFlag()
    {
        using var tempDir = new TempDirectory();
        var file = Path.Combine(tempDir.Path, "written-during-rebuild.txt");
        File.WriteAllText(file, "x");
        var (indexer, fixture) = CreateIndexer();
        indexer.Status.Drives.Single(d => d.Drive == "C").State = "indexing";
        indexer.ApplyFolderChange("C", WatcherChangeTypes.Created, file);

        FolderWalkRecorder.Replay(indexer, "C", CancellationToken.None, null);

        Assert.IsTrue(indexer.ConsumeMissedFolderChangeDuringRebuild("C"));
        Dispose(indexer, fixture);
    }

    // Keep hands the recorder to EnsureMonitor, which on a cold start runs only after every other drive's
    // walk. A Stop or removal request in that window still arrives as the walk token's cancel, and must
    // release the watcher then, not when the hand-over finally comes.
    [TestMethod]
    public void Record_CancelledAfterWalkBeforeHandOver_ReleasesTheWatcherAndMarksItLost()
    {
        using var indexer = new UsnIndexer();
        using var cts = new CancellationTokenSource();

        FolderWalkRecorder.Record(indexer, "Q", cts.Token, () => new FileRecordStore());
        cts.Cancel(); // after the walk, before EnsureMonitor's Replay
        FolderWalkRecorder recorder;
        lock (indexer.LockObj)
            recorder = indexer._walkRecorders["Q"];
        recorder.Monitor.Schedule(WatcherChangeTypes.Created, @"Q:\after-cancel.txt");
        var (changes, lost) = recorder.Stop();

        Assert.HasCount(0, changes, "still recording after the walk's token was cancelled");
        Assert.IsTrue(lost);
    }

    // "Safely remove" of a drive whose walk is done while other drives are still walking: the walk's removal
    // scope ended with that walk, and the live monitor's listener only attaches in EnsureMonitor, after every
    // drive's walk. The recorder's own listener must release the watcher. The PnP callback is raised by hand,
    // as DriveDeviceRemovalMonitorTests do; "Q" is unmounted, so nothing native is registered.
    [TestMethod]
    public void Record_RemovalRequestedAfterTheWalksRemovalScopeEnded_ReleasesTheKeptRecorder()
    {
        using var indexer = new UsnIndexer();
        using var startup = new CancellationTokenSource();
        Assert.IsFalse(Directory.Exists(@"Q:\"), "precondition: Q: must not be mounted");
        var scope = DriveIndexRemovalScope.Register("Q", startup.Token, () => { }, _ => { });
        FolderWalkRecorder.Record(indexer, "Q", startup.Token, () => new FileRecordStore());
        scope.Dispose(); // IndexBuilder's per-drive iteration ends; other drives are still walking
        FolderWalkRecorder recorder;
        lock (indexer.LockObj)
            recorder = indexer._walkRecorders["Q"];

        recorder.Removal.HandleNotification(IntPtr.Zero, IntPtr.Zero, DeviceNotificationNative.DeviceQueryRemove, IntPtr.Zero, 0);
        recorder.Monitor.Schedule(WatcherChangeTypes.Created, @"Q:\while-other-drives-walk.txt");
        // The removal was vetoed elsewhere, or the volume came back, by the time the monitors start.
        recorder.Removal.HandleNotification(IntPtr.Zero, IntPtr.Zero, DeviceNotificationNative.DeviceQueryRemoveFailed, IntPtr.Zero, 0);
        var requested = new List<string>();
        FolderWalkRecorder.Replay(indexer, "Q", startup.Token, requested.Add);

        Assert.HasCount(0, recorder.Stop().Changes, "the kept recorder ignored the removal request and kept recording");
        CollectionAssert.AreEqual(new[] { "Q" }, requested, "the watcher was released, so exactly one re-walk is owed");
    }

    // Cold start: the walk runs on its DriveIndexRemovalScope's token, whose source IndexBuilder disposes --
    // unlinking it from the startup's -- when that drive's walk ends. The recorder is registered on the
    // startup's own token instead, so a full rebuild or shutdown cancelling it before the hand-over still
    // releases the watcher.
    [TestMethod]
    public void Record_ParentCancelledAfterTheWalksRemovalScopeEnded_ReleasesTheKeptRecorder()
    {
        Assert.IsFalse(Directory.Exists(@"Q:\"), "precondition: Q: must not be mounted");
        using var indexer = new UsnIndexer();
        using var startup = new CancellationTokenSource();
        var scope = DriveIndexRemovalScope.Register("Q", startup.Token, () => { }, _ => { });
        FolderWalkRecorder.Record(indexer, "Q", startup.Token, () =>
        {
            scope.Token.ThrowIfCancellationRequested(); // the walk itself runs on the scope's token
            return new FileRecordStore();
        });
        scope.Dispose(); // IndexBuilder's `using var removalScope` ends with this drive's iteration
        FolderWalkRecorder recorder;
        lock (indexer.LockObj)
            recorder = indexer._walkRecorders["Q"];

        startup.Cancel();
        recorder.Monitor.Schedule(WatcherChangeTypes.Created, @"Q:\after-cancel.txt");
        var (changes, lost) = recorder.Stop();

        Assert.HasCount(0, changes, "still recording after the startup's token was cancelled");
        Assert.IsTrue(lost);
    }

    // A full rebuild starts while a per-drive rebuild of the same drive is past its walk but has not handed
    // over: the newer build's recording replaces the older one's. The older build's hand-over must leave the
    // newer recording alone, and since its own stopped early, ask for the walk that recovers the gap.
    [TestMethod]
    public void Replay_OfAnOlderBuild_LeavesTheNewerBuildsRecorderAndAsksForAReWalk()
    {
        using var tempDir = new TempDirectory();
        var drive = tempDir.Path; // a real root, so each recorder's watcher starts and loses nothing
        using var indexer = new UsnIndexer();
        using var older = new CancellationTokenSource();
        using var newer = new CancellationTokenSource();
        FolderWalkRecorder.Record(indexer, drive, older.Token, () => new FileRecordStore());
        FolderWalkRecorder.Record(indexer, drive, newer.Token, () => new FileRecordStore());
        var olderRequests = new List<string>();
        var newerRequests = new List<string>();

        FolderWalkRecorder.Replay(indexer, drive, older.Token, olderRequests.Add);
        bool newerStillKept;
        lock (indexer.LockObj)
            newerStillKept = indexer._walkRecorders.TryGetValue(drive, out var kept) && kept.Owner == newer.Token;
        FolderWalkRecorder.Replay(indexer, drive, newer.Token, newerRequests.Add);

        Assert.IsTrue(newerStillKept, "the older build's hand-over took the newer build's recording");
        CollectionAssert.AreEqual(new[] { drive }, olderRequests);
        Assert.IsEmpty(newerRequests);
    }

    // The same, with the newer build still walking: its recorder is registered from before its walk, so an
    // older build's hand-over landing mid-walk must not take and stop it.
    [TestMethod]
    public void Replay_OfAnOlderBuild_DoesNotStopTheRecorderOfAWalkStillRunning()
    {
        using var tempDir = new TempDirectory();
        var drive = tempDir.Path; // a real root, so each recorder's watcher starts and loses nothing
        using var indexer = new UsnIndexer();
        using var older = new CancellationTokenSource();
        using var newer = new CancellationTokenSource();
        var olderRequests = new List<string>();
        var newerRequests = new List<string>();
        FolderWalkRecorder.Record(indexer, drive, older.Token, () => new FileRecordStore()); // walk done
        var stillRecording = false;
        FolderWalkRecorder.Record(indexer, drive, newer.Token, () =>
        {
            FolderWalkRecorder.Replay(indexer, drive, older.Token, olderRequests.Add); // the older build's EnsureMonitor
            lock (indexer.LockObj)
                stillRecording = indexer._walkRecorders.TryGetValue(drive, out var current) && current.Owner == newer.Token;
            return new FileRecordStore();
        });
        FolderWalkRecorder.Replay(indexer, drive, newer.Token, newerRequests.Add);

        Assert.IsTrue(stillRecording, "another build's hand-over stopped the recorder of a walk still running");
        CollectionAssert.AreEqual(new[] { drive }, olderRequests, "the older build's own recording was cut short");
        Assert.IsEmpty(newerRequests);
    }

    // A buffer overflow on the drive's live monitor asks for a re-walk at the loss (watcher Error) and again
    // when the watcher comes back 10 s later (onStarted -> Recovered); #569's FlushPending catch asks too.
    // The first queues the drive's rebuild. Those landing while that rebuild's recorder is registered -- mid
    // walk ("indexing", set by BuildDrives) and between the walk and the hand-over (OnDriveCompleted has
    // marked the drive ready) -- must not queue another: the walk and its recorder already cover the loss.
    // The real maintenance would turn each forwarded one into a follow-up walk of that rebuild.
    [TestMethod]
    public void RequestFolderReindex_DuringTheDrivesOwnRebuild_QueuesNothingAndTheHandOverCoversIt()
    {
        using var tempDir = new TempDirectory();
        var drive = tempDir.Path; // a real root, so the recorder's watcher starts and loses nothing
        using var lifetime = new CancellationTokenSource();
        using var indexer = new UsnIndexer();
        indexer.Status.Drives.Add(new UsnIndexer.DriveIndexStatus { Drive = drive, State = "ready", Enabled = true });
        var heldWork = new List<Action>();
        var maintenance = new SearchEngineDriveMaintenance(indexer, () => new MachineSettings(),
            () => lifetime.Token, () => false, () => { }, TimeProvider.System, heldWork.Add);
        var forwarded = 0;
        void Forward(string d)
        {
            forwarded++;
            maintenance.QueueDriveRebuild(d);
        }
        using var rebuild = new CancellationTokenSource();

        Assert.IsTrue(maintenance.QueueDriveRebuild(drive, forceRebuild: false), "the overflow's own request");
        FolderWalkRecorder.Record(indexer, drive, rebuild.Token, () =>
        {
            indexer.RequestFolderReindex(drive, Forward); // mid-walk: the restarted watcher
            return new FileRecordStore();
        });
        indexer.SetDriveState(drive, "ready"); // OnDriveCompleted
        indexer.RequestFolderReindex(drive, Forward); // walk done, hand-over pending
        var followUps = new List<string>();
        FolderWalkRecorder.Replay(indexer, drive, rebuild.Token, followUps.Add);
        indexer.RequestFolderReindex(drive, Forward); // after the hand-over: the live monitor's own again
        lifetime.Cancel();

        Assert.AreEqual(1, forwarded, "only the request after the hand-over is the monitor's own");
        Assert.HasCount(1, heldWork);
        Assert.IsEmpty(followUps, "the recorder lost nothing, so the hand-over owes no walk");
    }

    // A build whose token is already cancelled -- a stale per-drive rebuild a full rebuild cancelled while it
    // was still on its way to its walk -- registers nothing. If it still hands over, nothing recorded its walk,
    // so it asks for the re-walk.
    [TestMethod]
    public void Record_TokenAlreadyCancelled_RegistersNothingAndItsHandOverAsksForAReWalk()
    {
        using var indexer = new UsnIndexer();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        FolderWalkRecorder.Record(indexer, "Q", cts.Token, () => new FileRecordStore());
        bool registered;
        lock (indexer.LockObj)
            registered = indexer._walkRecorders.ContainsKey("Q");
        var requested = new List<string>();
        FolderWalkRecorder.Replay(indexer, "Q", cts.Token, requested.Add);

        Assert.IsFalse(registered);
        CollectionAssert.AreEqual(new[] { "Q" }, requested);
    }

    // The final review's stale-build case: a full rebuild's walk of the drive is running (its recorder
    // registered) when a per-drive rebuild the full rebuild already cancelled reaches Record for the same
    // drive. That stale build must not displace the live recording: its walk throws, its build settles
    // (Discard takes only its own recorder, so nothing), and the full rebuild's hand-over still finds its
    // recording intact, with the change made during the walk.
    [TestMethod]
    public void Record_StaleCancelledBuild_DoesNotDisplaceTheRunningWalksRecording()
    {
        using var tempDir = new TempDirectory();
        var drive = tempDir.Path; // a real root, so the full rebuild's own watcher starts and loses nothing by itself
        using var indexer = new UsnIndexer();
        using var fullRebuild = new CancellationTokenSource();
        using var stalePerDrive = new CancellationTokenSource();
        stalePerDrive.Cancel(); // the lifetime cancel the full rebuild issued reached the per-drive rebuild
        var requests = new List<string>();
        FolderWalkRecorder? fullRebuildsRecorder = null;

        FolderWalkRecorder.Record(indexer, drive, fullRebuild.Token, () =>
        {
            lock (indexer.LockObj)
                fullRebuildsRecorder = indexer._walkRecorders[drive];
            try
            {
                FolderWalkRecorder.Record(indexer, drive, stalePerDrive.Token, () =>
                {
                    stalePerDrive.Token.ThrowIfCancellationRequested();
                    return new FileRecordStore();
                });
            }
            catch (OperationCanceledException)
            {
                // IndexBuilder files the drive as cancelled.
            }
            FolderWalkRecorder.Discard(indexer, drive, stalePerDrive.Token); // the stale build's SettleFailedBuild
            fullRebuildsRecorder!.Monitor.Schedule(WatcherChangeTypes.Created, Path.Combine(drive, "made-during-walk.txt"));
            return new FileRecordStore();
        });
        FolderWalkRecorder.Replay(indexer, drive, fullRebuild.Token, requests.Add); // the full rebuild's EnsureMonitor

        Assert.HasCount(1, fullRebuildsRecorder!.Stop().Changes, "the stale build cut the running walk's recording short");
        Assert.IsEmpty(requests);
    }

    // And if another build did displace this build's recording (it was not cancelled yet when it registered),
    // this build's hand-over finds no recording of its own -- even after the other build has settled and taken
    // its own away -- and asks for the re-walk instead of returning silently.
    [TestMethod]
    public void Replay_RecordingDisplacedByAnotherBuildThatThenSettled_AsksForAReWalk()
    {
        using var tempDir = new TempDirectory();
        var drive = tempDir.Path;
        using var indexer = new UsnIndexer();
        using var running = new CancellationTokenSource();
        using var other = new CancellationTokenSource();
        var requests = new List<string>();

        FolderWalkRecorder.Record(indexer, drive, running.Token, () =>
        {
            FolderWalkRecorder.Record(indexer, drive, other.Token, () =>
            {
                other.Cancel();
                return null;
            });
            FolderWalkRecorder.Discard(indexer, drive, other.Token); // the other build's SettleFailedBuild
            return new FileRecordStore();
        });
        FolderWalkRecorder.Replay(indexer, drive, running.Token, requests.Add);

        CollectionAssert.AreEqual(new[] { drive }, requests);
    }

    // A Stop that lands after the walk, once the build has swapped the walked index in (typically while
    // OnDriveCompleted writes the snapshot of a large drive), stops nothing any more: the build still hands
    // over. The cancel abandoned the recorder, so the hand-over applies what it recorded until then and asks
    // for the walk that recovers the rest.
    [TestMethod]
    public void Replay_AfterAStopThatCameOnceTheIndexWasSwappedIn_AppliesTheRecordingAndAsksForAReWalk()
    {
        using var tempDir = new TempDirectory();
        var file = Path.Combine(tempDir.Path, "made-during-walk.txt");
        File.WriteAllText(file, "x");
        var (indexer, fixture) = CreateIndexer();
        using var rebuild = new CancellationTokenSource();
        var recorder = new FolderWalkRecorder("C", rebuild.Token);
        FolderWalkRecorder.Keep(indexer, "C", recorder);
        recorder.Monitor.Schedule(WatcherChangeTypes.Created, file); // made behind the walker
        recorder.Monitor.FlushPending();
        rebuild.Cancel(); // Stop, after BuildDrives returned the drive's metadata
        var requests = new List<string>();

        FolderWalkRecorder.Replay(indexer, "C", rebuild.Token, requests.Add); // EnsureMonitor, unconditionally

        Assert.IsTrue(InIndex(fixture.Index, file, isDirectory: false));
        CollectionAssert.AreEqual(new[] { "C" }, requests);
        Dispose(indexer, fixture);
    }

    // A watcher that cannot start (root missing, or the start threw) raises no Error, and DriveWatcherHost only
    // retries it 10 s later: a walk shorter than that ran with nothing watching at all, so the recording is
    // incomplete even though no event was ever dropped.
    [TestMethod]
    public void Record_WatcherCannotStartBeforeWalk_MarksTheRecordingLost()
    {
        using var tempDir = new TempDirectory();
        var root = Path.Combine(tempDir.Path, "not-yet");
        using var indexer = new UsnIndexer();

        FolderWalkRecorder.Record(indexer, root, CancellationToken.None, () =>
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "made-during-walk.txt"), "x");
            return new FileRecordStore();
        });
        FolderWalkRecorder recorder;
        lock (indexer.LockObj)
            recorder = indexer._walkRecorders[root];
        var (changes, lost) = recorder.Stop();

        Assert.IsTrue(lost, $"watcher never started, recorded {changes.Count} change(s), yet Lost=false");
    }

    // The same gap through the hand-over: it must ask for a re-walk, which also stands in for the
    // missed-change flag the hand-over clears.
    [TestMethod]
    public void Replay_RecorderWhoseWatcherNeverStarted_RequestsAReWalk()
    {
        using var tempDir = new TempDirectory();
        var root = Path.Combine(tempDir.Path, "not-yet");
        using var indexer = new UsnIndexer();

        FolderWalkRecorder.Record(indexer, root, CancellationToken.None, () =>
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "made-during-walk.txt"), "x");
            return new FileRecordStore();
        });
        var requested = new List<string>();
        FolderWalkRecorder.Replay(indexer, root, CancellationToken.None, requested.Add);

        CollectionAssert.AreEqual(new[] { root }, requested);
    }

    private static (UsnIndexer Indexer, LiveIndexFixture Fixture) CreateIndexer()
    {
        var fixture = LiveIndexFixture.Build("C", new[] { LiveIndexFixture.Root() });
        var indexer = new UsnIndexer();
        indexer._recordIndexes["C"] = fixture.Index;
        indexer.Status.Drives.Add(new UsnIndexer.DriveIndexStatus { Drive = "C", State = "ready" });
        return (indexer, fixture);
    }

    private static bool InIndex(LiveIndex index, string path, bool isDirectory) =>
        index.Read((_, delta) => delta.Exists((UInt128)PathHelpers.HashPath64(PathHelpers.NormalizePath(path, isDirectory))));

    // The fixture owns (and disposes) its index; a second Dispose of it would throw.
    private static void Dispose(UsnIndexer indexer, LiveIndexFixture fixture)
    {
        indexer._recordIndexes.Clear();
        indexer.Dispose();
        fixture.Dispose();
    }

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("lertaro-tests-").FullName;
        public void Dispose() { try { Directory.Delete(Path, recursive: true); } catch { } }
    }
}
