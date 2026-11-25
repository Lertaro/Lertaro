using Lertaro.Core.DriveMonitoring;

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
        var monitor = new FolderDriveMonitor(root, (_, _, _) => applied.Set(), CancellationToken.None,
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
        var monitor = new FolderDriveMonitor("Z", (type, path, _) => applied.Add((type, path)), CancellationToken.None);
        return (monitor, applied);
    }
}
