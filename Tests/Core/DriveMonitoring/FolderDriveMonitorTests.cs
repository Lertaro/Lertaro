using Lertaro.Core.DriveMonitoring;
using Lertaro.Core.Indexer.Usn;
using Lertaro.Core.Tests.IndexV2;

namespace Lertaro.Core.Tests.DriveMonitoring;

// Schedule/FlushPending are driven directly, with the real debounce Timer Schedule arms. Only the
// start-failure test starts a real watcher.
[TestClass]
public sealed class FolderDriveMonitorTests
{
    private static readonly string DataDir = Path.TrimEndingDirectorySeparator(Logger.SharedDataDir);

    // An exception escaping a Timer callback terminates the process, so a failing change must turn into a
    // re-walk of the drive instead. If this regresses, the test host dies rather than the test failing.
    [TestMethod]
    public void Schedule_ChangeHandlerThrows_RequestsAReindex()
    {
        using var reindexRequested = new ManualResetEventSlim();
        using var monitor = new FolderDriveMonitor(
            Path.GetTempPath(),
            (_, _, _) => throw new IOException("The device is not ready."),
            CancellationToken.None,
            reindexRequested.Set);

        monitor.Schedule(WatcherChangeTypes.Created, Path.Combine(Path.GetTempPath(), "new-folder"));

        Assert.IsTrue(reindexRequested.Wait(TimeSpan.FromSeconds(10)));
    }

    [TestMethod]
    public void Schedule_RepeatedChangedOnOnePath_IsAppliedOnce()
    {
        var (monitor, applied) = Create();
        using (monitor)
        {
            monitor.Schedule(WatcherChangeTypes.Changed, @"Z:\a.txt");
            monitor.Schedule(WatcherChangeTypes.Changed, @"Z:\a.txt");
            monitor.Schedule(WatcherChangeTypes.Changed, @"Z:\A.TXT");
            monitor.FlushPending();
        }

        Assert.AreEqual(1, applied.Count);
    }

    // Apply reads the path's state at flush time, so dropping the Changed after a Deleted would leave a
    // file that was recreated in between removed from the index.
    [TestMethod]
    public void Schedule_ChangedAfterDeleted_IsKept()
    {
        var (monitor, applied) = Create();
        using (monitor)
        {
            monitor.Schedule(WatcherChangeTypes.Changed, @"Z:\a.txt");
            monitor.Schedule(WatcherChangeTypes.Deleted, @"Z:\a.txt");
            monitor.Schedule(WatcherChangeTypes.Changed, @"Z:\a.txt");
            monitor.FlushPending();
        }

        CollectionAssert.AreEqual(
            new[] { WatcherChangeTypes.Changed, WatcherChangeTypes.Deleted, WatcherChangeTypes.Changed },
            applied.Select(c => c.Type).ToArray());
    }

    // A Created directory pulls in its subtree, so it must never be swallowed by an earlier Changed.
    [TestMethod]
    public void Schedule_CreatedAfterChanged_IsKept()
    {
        var (monitor, applied) = Create();
        using (monitor)
        {
            monitor.Schedule(WatcherChangeTypes.Changed, @"Z:\folder");
            monitor.Schedule(WatcherChangeTypes.Created, @"Z:\folder");
            monitor.FlushPending();
        }

        Assert.AreEqual(2, applied.Count);
    }

    [TestMethod]
    public void Schedule_ChangedAfterFlush_IsAppliedAgain()
    {
        var (monitor, applied) = Create();
        using (monitor)
        {
            monitor.Schedule(WatcherChangeTypes.Changed, @"Z:\a.txt");
            monitor.FlushPending();
            monitor.Schedule(WatcherChangeTypes.Changed, @"Z:\a.txt");
            monitor.FlushPending();
        }

        Assert.AreEqual(2, applied.Count);
    }

    // #298: a portable install on the watched drive saw its own index rewrite as a change, forever.
    [TestMethod]
    public void Schedule_UnderOwnDataDirectory_IsIgnored()
    {
        var (monitor, applied) = Create();
        using (monitor)
        {
            monitor.Schedule(WatcherChangeTypes.Changed, Path.Combine(DataDir, "indexes", "Z.idx"));
            monitor.Schedule(WatcherChangeTypes.Renamed, Path.Combine(DataDir, "indexes", "Z.idx"), Path.Combine(DataDir, "indexes", "Z.idx.tmp"));
            monitor.Schedule(WatcherChangeTypes.Changed, DataDir + "Other" + Path.DirectorySeparatorChar + "x.txt");
            monitor.FlushPending();
        }

        Assert.AreEqual(1, applied.Count);
        Assert.AreEqual(DataDir + "Other" + Path.DirectorySeparatorChar + "x.txt", applied[0].Path);
    }

    // Moving a file into or out of the data directory still changes a path outside it.
    [TestMethod]
    public void Schedule_RenamedAcrossOwnDataDirectory_IsApplied()
    {
        var (monitor, applied) = Create();
        using (monitor)
        {
            monitor.Schedule(WatcherChangeTypes.Renamed, Path.Combine(DataDir, "x.txt"), @"Z:\x.txt");
            monitor.Schedule(WatcherChangeTypes.Renamed, @"Z:\x.txt", Path.Combine(DataDir, "x.txt"));
            monitor.FlushPending();
        }

        Assert.AreEqual(2, applied.Count);
    }

    // The hand-over disposes the old monitor and then reconciles. A flush of the old monitor still running
    // after Dispose returned could apply its Deleted(x) on top of the hand-over that restored x, so Dispose
    // waits for the callback in flight and the rest of that batch is never delivered.
    [TestMethod]
    public void Dispose_WhileAFlushIsDelivering_WaitsForItAndDeliversNothingMore()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var applied = new List<string>();
        var monitor = new FolderDriveMonitor("Z", (_, path, _) =>
        {
            if (path == @"Z:\first")
            {
                entered.Set();
                release.Wait();
            }
            lock (applied)
                applied.Add(path);
            return false;
        }, CancellationToken.None);
        monitor.Schedule(WatcherChangeTypes.Changed, @"Z:\first");
        monitor.Schedule(WatcherChangeTypes.Deleted, @"Z:\x.txt");
        var flush = Task.Run(monitor.FlushPending);
        Assert.IsTrue(entered.Wait(TimeSpan.FromSeconds(30)), "flush never started");

        var dispose = Task.Run(monitor.Dispose);
        var returnedWhileBlocked = dispose.Wait(TimeSpan.FromSeconds(1));
        release.Set();

        Assert.IsTrue(flush.Wait(TimeSpan.FromSeconds(30)) && dispose.Wait(TimeSpan.FromSeconds(30)), "did not finish");
        Assert.IsFalse(returnedWhileBlocked, "Dispose returned while a flush was still delivering");
        CollectionAssert.AreEqual(new[] { @"Z:\first" }, applied);
    }

    // A watcher that could not start raises no Error, so nothing armed the loss gate and the retry that
    // finally brought it up asked for no re-walk: missed.txt, created while nothing watched, stayed missing.
    // Real watcher and DriveWatcherHost's real 10 s retry, so this one takes that long.
    [TestMethod]
    public void Start_RootMissingThenAppears_RequestsOneReWalkWhenTheWatcherComesUp()
    {
        var root = Path.Combine(Directory.CreateTempSubdirectory("lertaro-tests-").FullName, "not-yet");
        using var applied = new ManualResetEventSlim();
        var reindexRequests = 0;
        var monitor = new FolderDriveMonitor(root, (_, _, _) => { applied.Set(); return false; }, CancellationToken.None,
            () => Interlocked.Increment(ref reindexRequests));
        try
        {
            monitor.Start();
            Assert.AreEqual(0, Volatile.Read(ref reindexRequests), "asked for a walk before there was a root to walk");
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "missed.txt"), "x"); // nothing watches yet
            // Poke the root until the retry's watcher reports something: that proves it is up.
            var deadline = DateTime.UtcNow.AddSeconds(25);
            var i = 0;
            while (!applied.IsSet && DateTime.UtcNow < deadline)
            {
                File.WriteAllText(Path.Combine(root, $"probe{i++}.txt"), "x");
                applied.Wait(500);
            }

            Assert.IsTrue(applied.IsSet, "precondition: the retry never started the watcher");
            Assert.AreEqual(1, Volatile.Read(ref reindexRequests), "missed.txt was created while nothing watched");
        }
        finally
        {
            monitor.Dispose();
            try { Directory.Delete(Path.GetDirectoryName(root)!, recursive: true); } catch { }
        }
    }

    private static (FolderDriveMonitor Monitor, List<(WatcherChangeTypes Type, string Path)> Applied) Create()
    {
        var applied = new List<(WatcherChangeTypes, string)>();
        var monitor = new FolderDriveMonitor("Z", (type, path, _) => { applied.Add((type, path)); return false; }, CancellationToken.None);
        return (monitor, applied);
    }

    // A folder moved in whose walk fails part-way owes a re-walk, asked for once after the whole batch.
    // QueueDriveRebuild marks the drive "indexing" at once, so asking in the middle of the batch would make
    // the rest of it (here the Changed Windows raises on the folder the new one landed in) look missed by
    // that rebuild, and buy a second walk before the first has started.
    //
    // Only the debounce Timer sets a batch's boundary (250 ms after the last Schedule), so the two Schedule
    // calls below land in one batch unless this thread stalls for 250 ms between them. Such a split looks
    // exactly like asking mid-batch (the request comes before the second change), so an attempt that saw
    // it is retried with fresh state; asking mid-batch fails every attempt.
    [TestMethod]
    public void Flush_ABatchThatOwesARewalk_AsksOnceAfterTheBatch()
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            if (TryABatchThatOwesARewalk())
                return;
        }
        Assert.Fail("The re-walk was asked for before the rest of its batch was applied, in every attempt.");
    }

    private static bool TryABatchThatOwesARewalk()
    {
        using var temp = new TempDirectory();
        var drop = CreateFolderWithAChildThatFailsWhileBeingRead(temp.Path);
        using var fixture = LiveIndexFixture.Build("C", new[] { LiveIndexFixture.Root() });
        var indexer = new UsnIndexer(); // not disposed: it would dispose the fixture's LiveIndex a second time
        indexer._recordIndexes["C"] = fixture.Index;
        indexer.Status.Drives.Add(new UsnIndexer.DriveIndexStatus { Drive = "C", State = "ready" });
        var queued = new List<Action>();
        var maintenance = new SearchEngineDriveMaintenance(indexer, () => new MachineSettings(),
            () => CancellationToken.None, () => false, () => { }, TimeProvider.System, queued.Add);
        var accepted = new List<bool>();
        var order = new System.Collections.Concurrent.ConcurrentQueue<string>();
        using var asked = new SemaphoreSlim(0);
        using var applied = new SemaphoreSlim(0);
        using var monitor = new FolderDriveMonitor(
            Path.GetTempPath(),
            (changeType, path, oldPath) =>
            {
                var owesRewalk = indexer.ApplyFolderChange("C", changeType, path, oldPath);
                order.Enqueue("apply");
                applied.Release();
                return owesRewalk;
            },
            CancellationToken.None,
            () =>
            {
                accepted.Add(maintenance.QueueDriveRebuild("C", forceRebuild: false));
                order.Enqueue("ask");
                asked.Release();
            });

        monitor.Schedule(WatcherChangeTypes.Created, drop);
        monitor.Schedule(WatcherChangeTypes.Changed, temp.Path);
        Assert.IsTrue(applied.Wait(TimeSpan.FromSeconds(10)) && applied.Wait(TimeSpan.FromSeconds(10)));
        Assert.IsTrue(asked.Wait(TimeSpan.FromSeconds(10)));
        if (!order.SequenceEqual(new[] { "apply", "apply", "ask" }))
            return false;

        CollectionAssert.AreEqual(new[] { true }, accepted);
        Assert.IsFalse(indexer.ConsumeMissedFolderChangeDuringRebuild("C"),
            "The rest of the batch was flagged missed, so the one unreadable entry costs a second re-walk.");

        // While that rebuild is pending, main's QueueDriveRebuild refuses the next request as a duplicate
        // (#568 turns it into a follow-up). The drive reads "indexing", so the missed flag carries the debt.
        monitor.Schedule(WatcherChangeTypes.Created, drop);
        Assert.IsTrue(applied.Wait(TimeSpan.FromSeconds(10)));
        Assert.IsTrue(asked.Wait(TimeSpan.FromSeconds(10)));
        CollectionAssert.AreEqual(new[] { true, false }, accepted);
        Assert.AreEqual(1, queued.Count);
        Assert.IsTrue(indexer.ConsumeMissedFolderChangeDuringRebuild("C"));
        return true;
    }

    // An owed re-walk with nothing applied (a Created entry whose stat fails; a name no Win32 path may carry
    // is the deterministic stand-in) during a pending rebuild: the request is refused as a duplicate, so the
    // missed flag has to carry the debt.
    [TestMethod]
    public void Flush_OwedRewalkWithNothingChangedDuringAPendingRebuild_IsNotDropped()
    {
        using var temp = new TempDirectory();
        using var fixture = LiveIndexFixture.Build("C", new[] { LiveIndexFixture.Root() });
        var indexer = new UsnIndexer(); // not disposed: it would dispose the fixture's LiveIndex a second time
        indexer._recordIndexes["C"] = fixture.Index;
        indexer.Status.Drives.Add(new UsnIndexer.DriveIndexStatus { Drive = "C", State = "ready" });
        var queued = new List<Action>();
        var maintenance = new SearchEngineDriveMaintenance(indexer, () => new MachineSettings(),
            () => CancellationToken.None, () => false, () => { }, TimeProvider.System, queued.Add);
        Assert.IsTrue(maintenance.QueueDriveRebuild("C", forceRebuild: false)); // drive now "indexing"
        var accepted = new List<bool>();
        using var asked = new ManualResetEventSlim();
        using var monitor = new FolderDriveMonitor(
            Path.GetTempPath(),
            (changeType, path, oldPath) => indexer.ApplyFolderChange("C", changeType, path, oldPath),
            CancellationToken.None,
            () => { accepted.Add(maintenance.QueueDriveRebuild("C", forceRebuild: false)); asked.Set(); });

        monitor.Schedule(WatcherChangeTypes.Created, Path.Combine(temp.Path, "a?b"));
        Assert.IsTrue(asked.Wait(TimeSpan.FromSeconds(10)));

        Assert.IsTrue(accepted.Single() || indexer.ConsumeMissedFolderChangeDuringRebuild("C"),
            "The owed re-walk was refused as a duplicate and not flagged missed: it is lost.");
    }

    // The debt belongs to the drive, not the monitor. DriveMonitorFactory.EnsureMonitor disposes the old
    // monitor without cancelling its token when it replaces it after the drive's rebuild, and a change this
    // monitor already applied is one the replacement never hears about again.
    [TestMethod]
    public void Flush_MonitorReplacedWhileItsBatchIsApplied_StillAsksForTheOwedRewalk()
    {
        using var reindexRequested = new ManualResetEventSlim();
        FolderDriveMonitor? monitor = null;
        monitor = new FolderDriveMonitor(
            Path.GetTempPath(),
            (_, _, _) =>
            {
                monitor!.Dispose(); // replaced mid-flush
                return true;
            },
            CancellationToken.None,
            reindexRequested.Set);
        try
        {
            monitor.Schedule(WatcherChangeTypes.Created, Path.Combine(Path.GetTempPath(), "drop"));

            Assert.IsTrue(reindexRequested.Wait(TimeSpan.FromSeconds(10)),
                "The change was applied and owes a re-walk, but the request was dropped because the monitor was replaced.");
        }
        finally
        {
            monitor.Dispose();
        }
    }

    // A cancelled token is shutdown: nothing is asked for, even for a change that owed a re-walk.
    [TestMethod]
    public void Flush_TokenCancelledWhileItsBatchIsApplied_AsksNothing()
    {
        using var cts = new CancellationTokenSource();
        using var applied = new ManualResetEventSlim();
        var asked = 0;
        using var monitor = new FolderDriveMonitor(
            Path.GetTempPath(),
            (_, _, _) =>
            {
                cts.Cancel();
                applied.Set();
                return true;
            },
            cts.Token,
            () => Interlocked.Increment(ref asked));

        monitor.Schedule(WatcherChangeTypes.Created, Path.Combine(Path.GetTempPath(), "drop"));
        Assert.IsTrue(applied.Wait(TimeSpan.FromSeconds(10)));
        Thread.Sleep(200);

        Assert.AreEqual(0, Volatile.Read(ref asked));
    }

    // The monitor does not decide for itself whether the drive's rebuild covers the change: the drive can
    // read "ready" while its rebuild is still pending (after TryLoadDriveFromCache, or after
    // OnDriveCompleted until RebuildDrive's finally). The request goes to the callback, and
    // SearchEngineDriveMaintenance decides; turning a request during a pending rebuild into a follow-up is
    // #568's change to QueueDriveRebuild.
    [TestMethod]
    public void Flush_OwedRewalkWhileTheDrivesRebuildIsStillPending_ReachesTheReindexCallback()
    {
        using var temp = new TempDirectory();
        var drop = CreateFolderWithAChildThatFailsWhileBeingRead(temp.Path);
        using var fixture = LiveIndexFixture.Build("C", new[] { LiveIndexFixture.Root() });
        var indexer = new UsnIndexer(); // not disposed: it would dispose the fixture's LiveIndex a second time
        indexer._recordIndexes["C"] = fixture.Index;
        indexer.Status.Drives.Add(new UsnIndexer.DriveIndexStatus { Drive = "C", State = "ready" });
        var maintenance = new SearchEngineDriveMaintenance(indexer, () => new MachineSettings(),
            () => CancellationToken.None, () => false, () => { }, TimeProvider.System, _ => { });
        var queued = new List<Action>();
        Assert.IsTrue(maintenance.QueueDriveRebuild("C", forceRebuild: false));
        indexer.SetDriveState("C", "ready");
        using var reindexRequested = new ManualResetEventSlim();
        using var monitor = new FolderDriveMonitor(
            Path.GetTempPath(),
            (changeType, path, oldPath) => indexer.ApplyFolderChange("C", changeType, path, oldPath),
            CancellationToken.None,
            reindexRequested.Set);

        monitor.Schedule(WatcherChangeTypes.Created, drop);

        Assert.IsTrue(reindexRequested.Wait(TimeSpan.FromSeconds(10)));
    }

    private static string CreateFolderWithAChildThatFailsWhileBeingRead(string parent)
    {
        var drop = Path.Combine(parent, "drop");
        Directory.CreateDirectory(Path.Combine(drop, "foo"));
        File.WriteAllText(@"\\?\" + Path.Combine(drop, "foo "), "x");
        File.WriteAllText(Path.Combine(drop, "zzz.txt"), "x");
        return drop;
    }

    // "foo " has a trailing space, so only the \\?\ spelling deletes it.
    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("lertaro-folder-monitor-tests-").FullName;
        public void Dispose() { try { Directory.Delete(@"\\?\" + Path, recursive: true); } catch { } }
    }
}
