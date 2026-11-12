using Lertaro.Core.Indexer.NetworkDrive.Scheduling;

namespace Lertaro.Core.Tests.Indexer.NetworkDrive.Scheduling;

[TestClass]
public sealed class SchedulerTests
{
    [TestMethod]
    public void StartRefresh_RemovesIdleManualRootWatcher_AndKeepsRemainingRoot()
    {
        var removed = new List<string>();
        using var scheduler = new Scheduler((_, _) => { }, removed.Add, (_, _, _, _) => { },
            (_, _) => Assert.Fail("Cached manual roots need no initial scan."),
            (_, _, _, _) => Assert.Fail("Cached manual roots need no checkpoint."),
            _ => null, _ => { });
        var modes = new Dictionary<string, string> { [@"Z:\Archive"] = "Manual", [@"Y:\Photos"] = "Manual" };
        var cached = modes.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);

        scheduler.StartRefresh([@"Z:\Archive", @"Y:\Photos"], modes, cached);
        scheduler.StartRefresh([@"y:\photos"], modes, cached);
        scheduler.StartRefresh([@"Y:\Photos"], modes, cached);

        CollectionAssert.AreEqual(new[] { @"Z:\Archive" }, removed);
    }

    [TestMethod]
    [DataRow(false, "Manual", true)]
    [DataRow(true, "Manual", false)]
    [DataRow(true, "startup", true)]
    public void StartRefresh_WatcherReconciliationReflectsCacheBeforeScan(bool cached, string mode, bool expectedReconcile)
    {
        var requests = new List<(string Drive, bool Reconcile)>();
        using var scheduler = new Scheduler((drive, reconcile) => requests.Add((drive, reconcile)), _ => { },
            (_, _, _, _) => { }, (_, _) => { }, (_, _, _, _) => { }, _ => null, _ => { });
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        scheduler.StartRefresh([root], new Dictionary<string, string> { [root] = mode },
            cached ? new HashSet<string>(StringComparer.OrdinalIgnoreCase) { root } : []);
        scheduler.CancelDrive(root);

        Assert.AreEqual((root, expectedReconcile), Assert.ContainsSingle(requests));
    }

    [TestMethod]
    public void QueueRefreshDrive_UnconfiguredRoot_DoesNotEnqueueLateRecovery()
    {
        using var lifetime = new CancellationTokenSource();
        var debouncing = new Dictionary<string, CancellationTokenSource>(StringComparer.OrdinalIgnoreCase);
        var runner = new SchedulerQueueRunner(new object(), debouncing,
            new(StringComparer.OrdinalIgnoreCase), new(StringComparer.OrdinalIgnoreCase),
            new(StringComparer.OrdinalIgnoreCase), lifetime,
            (_, _, _, _) => Assert.Fail("A removed root must not report progress."),
            _ => throw new AssertFailedException("A removed root must not load a cache."),
            (_, _, _, _) => Assert.Fail("A removed root must not publish a checkpoint."),
            (_, _) => Assert.Fail("A removed root must not publish an index."),
            _ => Assert.Fail("A removed root must not release an index."));

        runner.QueueRefreshDrive(@"Z:\Archive", "watcher recovered, reconcile the outage");

        Assert.IsEmpty(debouncing, "Late watcher callbacks must not schedule any work for a removed root.");
    }
}
