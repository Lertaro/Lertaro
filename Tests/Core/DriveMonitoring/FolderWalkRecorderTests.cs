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
    public void Replay_AppliesWhatChangedDuringTheWalkToTheBuiltIndex_Once()
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

        FolderWalkRecorder.Replay(indexer, "C", _ => reindexRequests++);
        FolderWalkRecorder.Replay(indexer, "C", _ => reindexRequests++);

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

        FolderWalkRecorder.Replay(indexer, "C", requested.Add);

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

        FolderWalkRecorder.Replay(indexer, "C", null);

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

        FolderWalkRecorder.Replay(indexer, "C", null);

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

        FolderWalkRecorder.Replay(indexer, "C", null);

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

        FolderWalkRecorder.Replay(indexer, "C", null);

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

        FolderWalkRecorder.Replay(indexer, "C", null);

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

        FolderWalkRecorder.Replay(indexer, "C", null);

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
        var replay = new Thread(() => FolderWalkRecorder.Replay(indexer, "C", null)) { IsBackground = true };
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

        FolderWalkRecorder.Replay(indexer, "C", null);

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
            FolderWalkRecorder.Replay(indexer, "C", requested.Add);
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

        FolderWalkRecorder.Replay(indexer, "C", null);

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

        DriveArrivalRecovery.SettleFailedBuild(indexer, "C", wasCancelled: false, CancellationToken.None, null);

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

        DriveMonitorFactory.EnsureMonitor(indexer, "Q", 0, 0, cts.Token, null);

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
        FolderWalkRecorder.Replay(indexer, "Q", requested.Add);

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

        FolderWalkRecorder.Replay(indexer, "C", null);

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

        FolderWalkRecorder.Replay(indexer, "C", null);

        Assert.IsTrue(indexer.ConsumeMissedFolderChangeDuringRebuild("C"));
        Dispose(indexer, fixture);
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
