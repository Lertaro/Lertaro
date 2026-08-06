using Lertaro.Core.Hook;

namespace Lertaro.Core.Tests.Hook;

/// <summary>
/// The scheduler exists to keep a burst of window-movement events from turning into a burst of blocking
/// cross-process calls, so what is asserted here is how many times the work actually runs.
/// </summary>
[TestClass]
public class QuietPeriodSchedulerTests
{
    private const int QuietMs = 40;

    // How long to wait for something that is supposed to happen. Generous because it is only ever reached
    // when the test is about to fail anyway: a passing run returns as soon as the signal arrives, so
    // raising this costs nothing and buys tolerance for a machine running nineteen test assemblies at
    // once. Waiting a fixed period and then asserting -- what this class used to do -- fails on a loaded
    // machine for no reason other than the load.
    private const int WaitForRunMs = 10_000;

    // How long to wait before concluding something did NOT happen. A negative can only be established by
    // waiting, and load can only ever delay a run that should not occur at all, so this is not a source of
    // false failures the way the positive waits were.
    private const int SettleMs = 400;

    // The burst test needs the whole burst to fit inside one quiet period, which is a claim about wall
    // clock however it is written. Two hundred Timer.Change calls take microseconds, so a window this wide
    // tolerates being descheduled for most of a second mid-loop -- where the original 40ms did not, and a
    // burst straddling the period fired a run partway through and failed the test.
    private const int BurstQuietMs = 750;

    [TestMethod]
    public void RunsImmediatelyWhenAskedTo()
    {
        var runs = 0;
        using var scheduler = new QuietPeriodScheduler(() => Interlocked.Increment(ref runs), QuietMs);

        scheduler.RunNow();

        Assert.AreEqual(1, runs);
    }

    [TestMethod]
    public void DoesNotRunAtOnceWhenDeferred()
    {
        var runs = 0;
        using var scheduler = new QuietPeriodScheduler(() => Interlocked.Increment(ref runs), QuietMs);

        scheduler.RunWhenQuiet();

        Assert.AreEqual(0, runs);
    }

    [TestMethod]
    public void ABurstOfDeferredRequestsProducesASingleRun()
    {
        // The whole point: resizing a window emitted ~200 location changes a second, each of which used to
        // poll the tracked window over a synchronous cross-process call.
        var runs = 0;
        var ran = new ManualResetEventSlim();
        using var scheduler = new QuietPeriodScheduler(
            () => { Interlocked.Increment(ref runs); ran.Set(); }, BurstQuietMs);

        for (var i = 0; i < 200; i++)
            scheduler.RunWhenQuiet();

        Assert.AreEqual(0, runs, "nothing should run while the requests are still arriving");
        Assert.IsTrue(ran.Wait(WaitForRunMs), "the burst never produced its run");
        Assert.AreEqual(1, runs, "two hundred requests should collapse into one run");
    }

    [TestMethod]
    public void ADeferredRunHappensOnceTheBurstStops()
    {
        var runs = 0;
        var ran = new ManualResetEventSlim();
        using var scheduler = new QuietPeriodScheduler(
            () => { Interlocked.Increment(ref runs); ran.Set(); }, QuietMs);

        scheduler.RunWhenQuiet();

        Assert.IsTrue(ran.Wait(WaitForRunMs), "the deferred run never happened");
        Assert.AreEqual(1, runs);
    }

    [TestMethod]
    public void AnImmediateRunDropsAPendingDeferredOne()
    {
        // It answers whatever the pending one was going to ask, so letting it fire too would be one more
        // trip into the tracked window for nothing.
        var runs = 0;
        using var scheduler = new QuietPeriodScheduler(() => Interlocked.Increment(ref runs), QuietMs);

        scheduler.RunWhenQuiet();
        scheduler.RunNow();

        // A negative: waiting is how it gets established. If the dropped request fired after all, runs
        // becomes 2 within a quiet period, and this wait is many times that.
        Thread.Sleep(SettleMs);
        Assert.AreEqual(1, runs);
    }

    [TestMethod]
    public void CancelDropsAPendingDeferredRun()
    {
        var runs = 0;
        using var scheduler = new QuietPeriodScheduler(() => Interlocked.Increment(ref runs), QuietMs);

        scheduler.RunWhenQuiet();
        scheduler.Cancel();

        Thread.Sleep(SettleMs);
        Assert.AreEqual(0, runs);
    }

    [TestMethod]
    public void RunsNeverOverlap()
    {
        // Deferred runs arrive on a timer thread while immediate ones stay on the caller's, which is a new
        // way for two to meet -- the work used to be single-threaded.
        var entered = new ManualResetEventSlim();
        var release = new ManualResetEventSlim();
        var concurrent = 0;
        var maxConcurrent = 0;

        using var scheduler = new QuietPeriodScheduler(() =>
        {
            var now = Interlocked.Increment(ref concurrent);
            InterlockedMax(ref maxConcurrent, now);
            entered.Set();
            release.Wait(SettleMs);
            Interlocked.Decrement(ref concurrent);
        }, QuietMs);

        var blocker = Task.Run(scheduler.RunNow);
        Assert.IsTrue(entered.Wait(WaitForRunMs), "the first run never started");

        scheduler.RunNow(); // must not join the run already in progress
        release.Set();
        Assert.IsTrue(blocker.Wait(WaitForRunMs), "the blocking run never finished");

        Assert.AreEqual(1, maxConcurrent);
    }

    [TestMethod]
    public void ARunSkippedForBeingBusyIsRetried()
    {
        // Skipping without a retry would silently drop the refresh that request was asking for.
        var release = new ManualResetEventSlim();
        var runs = 0;
        var firstRun = new ManualResetEventSlim();
        var retried = new ManualResetEventSlim();

        using var scheduler = new QuietPeriodScheduler(() =>
        {
            if (Interlocked.Increment(ref runs) == 1)
            {
                firstRun.Set();
                release.Wait(WaitForRunMs);
            }
            else
            {
                retried.Set();
            }
        }, QuietMs);

        var blocker = Task.Run(scheduler.RunNow);
        Assert.IsTrue(firstRun.Wait(WaitForRunMs), "the first run never started");

        scheduler.RunNow();       // skipped: the first is still holding the lock
        Assert.AreEqual(1, runs);

        release.Set();
        Assert.IsTrue(blocker.Wait(WaitForRunMs), "the blocking run never finished");

        // Waited for rather than slept past: the skipped request re-arms itself on the quiet period, and
        // how long that takes to be picked up is not something to assume on a loaded machine.
        Assert.IsTrue(retried.Wait(WaitForRunMs), "the skipped request was never retried");
        Assert.AreEqual(2, runs);
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int seen;
        while ((seen = Volatile.Read(ref target)) < value)
        {
            if (Interlocked.CompareExchange(ref target, value, seen) == seen)
                return;
        }
    }
}
