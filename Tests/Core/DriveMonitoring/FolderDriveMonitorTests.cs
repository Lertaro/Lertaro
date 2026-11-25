using Lertaro.Core.DriveMonitoring;
using Lertaro.Core.Indexer.Usn;
using Lertaro.Core.Tests.IndexV2;

namespace Lertaro.Core.Tests.DriveMonitoring;

// The watcher itself is never started here: Schedule is what its event handlers call, and the debounce
// Timer it arms is the real one.
[TestClass]
public sealed class FolderDriveMonitorTests
{
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
        var maintenance = new SearchEngineDriveMaintenance(indexer, () => new MachineSettings(),
            () => CancellationToken.None, () => false, () => { });
        var queued = new List<Action>();
        var accepted = new List<bool>();
        var order = new System.Collections.Concurrent.ConcurrentQueue<string>();
        using var asked = new SemaphoreSlim(0);
        using var applied = new SemaphoreSlim(0);
        using var monitor = new FolderDriveMonitor(
            Path.GetTempPath(),
            (changeType, path, oldPath) =>
            {
                var owesRewalk = indexer.ApplyFolderChange("C", changeType, path, oldPath, () => { });
                order.Enqueue("apply");
                applied.Release();
                return owesRewalk;
            },
            CancellationToken.None,
            () =>
            {
                accepted.Add(maintenance.QueueDriveRebuild("C", forceRebuild: false, queued.Add));
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
        var maintenance = new SearchEngineDriveMaintenance(indexer, () => new MachineSettings(),
            () => CancellationToken.None, () => false, () => { });
        var queued = new List<Action>();
        Assert.IsTrue(maintenance.QueueDriveRebuild("C", forceRebuild: false, queued.Add)); // drive now "indexing"
        var accepted = new List<bool>();
        using var asked = new ManualResetEventSlim();
        using var monitor = new FolderDriveMonitor(
            Path.GetTempPath(),
            (changeType, path, oldPath) => indexer.ApplyFolderChange("C", changeType, path, oldPath, () => { }),
            CancellationToken.None,
            () => { accepted.Add(maintenance.QueueDriveRebuild("C", forceRebuild: false, queued.Add)); asked.Set(); });

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
            () => CancellationToken.None, () => false, () => { });
        var queued = new List<Action>();
        Assert.IsTrue(maintenance.QueueDriveRebuild("C", forceRebuild: false, queued.Add));
        indexer.SetDriveState("C", "ready");
        using var reindexRequested = new ManualResetEventSlim();
        using var monitor = new FolderDriveMonitor(
            Path.GetTempPath(),
            (changeType, path, oldPath) => indexer.ApplyFolderChange("C", changeType, path, oldPath, () => { }),
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
