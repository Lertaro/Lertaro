using System.Threading.Channels;
using Lertaro.Core.DriveMonitoring;

namespace Lertaro.Core.Tests.DriveMonitoring;

// The arrival notification itself is PnP plumbing with no seam (a real device interface and a real
// Configuration Manager callback), so what is pinned here is the loop around it: an arrival event wakes
// the waiter without waiting for the fallback probe, the few probes after an event catch the letter that
// trails the interface, and nothing about a missing drive can kill the waiter.
[TestClass]
public sealed class DriveReattachWaiterTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [TestMethod]
    public async Task WatchAsync_DriveIsAlreadyPresent_ReattachesWithoutWaiting()
    {
        var arrivals = new Arrivals();
        var reattached = 0;

        await DriveReattachWaiter.WatchAsync(
            "Z",
            () => true,
            arrivals.Wait,
            () => reattached++,
            CancellationToken.None).WaitAsync(Timeout);

        Assert.AreEqual(1, reattached);
    }

    [TestMethod]
    public async Task WatchAsync_ArrivalEvent_WakesTheLoopBeforeTheFallbackProbe()
    {
        var arrivals = new Arrivals();
        var present = false;
        var reattached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cts = new CancellationTokenSource(Timeout);

        var watch = DriveReattachWaiter.WatchAsync(
            "Z",
            () => present,
            arrivals.Wait,
            () => reattached.TrySetResult(),
            cts.Token);

        await Task.Delay(TimeSpan.FromMilliseconds(100));
        Assert.IsFalse(reattached.Task.IsCompleted, "No event yet, so the loop must still be waiting.");

        arrivals.Fire();
        // The letter trails the volume interface; the loop's post-arrival probes are what allow this
        // to be observed within the same wake-up rather than after the fallback interval.
        present = true;

        await reattached.Task.WaitAsync(Timeout);
        await watch.WaitAsync(Timeout);
    }

    [TestMethod]
    public async Task WatchAsync_FallbackProbe_KeepsLookingUntilTheDriveAppears()
    {
        var arrivals = new Arrivals();
        var present = false;
        var reattached = 0;
        using var cts = new CancellationTokenSource(Timeout);

        var watch = DriveReattachWaiter.WatchAsync(
            "Z",
            () => present,
            arrivals.Wait,
            () => reattached++,
            cts.Token);

        arrivals.Tick(); // a fallback timeout: false, so the loop probes again
        await Task.Delay(TimeSpan.FromMilliseconds(100));
        present = true;
        arrivals.Tick();

        await watch.WaitAsync(Timeout);
        Assert.AreEqual(1, reattached);
    }

    [TestMethod]
    public async Task WatchAsync_Cancelled_StopsWithoutReattaching()
    {
        var arrivals = new Arrivals();
        var reattached = 0;
        using var cts = new CancellationTokenSource();

        var watch = DriveReattachWaiter.WatchAsync(
            "Z",
            () => false,
            arrivals.Wait,
            () => reattached++,
            cts.Token);

        await Task.Delay(TimeSpan.FromMilliseconds(100));
        cts.Cancel();

        await watch.WaitAsync(Timeout);
        Assert.AreEqual(0, reattached);
    }

    [TestMethod]
    public async Task WatchAsync_ProbeThrows_LogsAndKeepsWaiting()
    {
        var arrivals = new Arrivals();
        var attempts = 0;
        var reattached = 0;
        using var cts = new CancellationTokenSource(Timeout);

        var watch = DriveReattachWaiter.WatchAsync(
            "Z",
            () =>
            {
                if (Interlocked.Increment(ref attempts) == 1)
                    throw new IOException("the volume handle was not ready");
                return true;
            },
            arrivals.Wait,
            () => reattached++,
            cts.Token);

        await watch.WaitAsync(Timeout);
        Assert.AreEqual(1, reattached);
    }

    private sealed class Arrivals
    {
        private readonly Channel<bool> _channel = Channel.CreateUnbounded<bool>();

        public Task<bool> Wait(CancellationToken token) => _channel.Reader.ReadAsync(token).AsTask();

        /// <summary>An arrival event: wakes the loop for its post-arrival probe window.</summary>
        public void Fire() => _channel.Writer.TryWrite(true);

        /// <summary>A fallback timeout: no event, the loop just probes again.</summary>
        public void Tick() => _channel.Writer.TryWrite(false);
    }
}
