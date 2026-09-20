using System.Diagnostics;
using Lertaro.Core.DriveMonitoring;

namespace Lertaro.Core.Tests.DriveMonitoring;

// MonitorLoopJoin is the bounded wait that keeps a drive monitor's loop out of the LiveIndex the service
// stop path disposes right after the monitors (see the helper's own comment for the field failure it
// fixes). Its whole behaviour is a mapping from "how does this Task end" to "what do I return, and how
// long do I wait", so it is exercised with plain in-memory Tasks rather than a real volume.
[TestClass]
public sealed class MonitorLoopJoinTests
{
    private static readonly TimeSpan ShortTimeout = TimeSpan.FromMilliseconds(200);

    [TestMethod]
    public void CompletedLoop_ReturnsTrue() =>
        Assert.IsTrue(MonitorLoopJoin.Wait(Task.CompletedTask, "C"));

    [TestMethod]
    public void LoopFinishingInsideTheWindow_ReturnsTrue()
    {
        var finishing = Task.Delay(30);

        Assert.IsTrue(MonitorLoopJoin.Wait(finishing, "C", TimeSpan.FromSeconds(5)));
    }

    [TestMethod]
    public void LoopStillRunning_GivesUpAfterTheTimeoutInsteadOfBlockingTheStop()
    {
        var running = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stopwatch = Stopwatch.StartNew();

        try
        {
            Assert.IsFalse(MonitorLoopJoin.Wait(running.Task, "C", ShortTimeout));

            // The loop must be left running, not force-killed or corrupted, and the wait must actually have
            // consumed the timeout -- returning false immediately would dispose the index while the loop is
            // still applying a batch into it.
            Assert.IsFalse(running.Task.IsCompleted);
            Assert.IsGreaterThanOrEqualTo((long)(ShortTimeout.TotalMilliseconds * 0.8), (long)stopwatch.Elapsed.TotalMilliseconds);
        }
        finally
        {
            running.SetResult();
        }
    }

    [TestMethod]
    public void FaultedLoop_CountsAsStoppedAndDoesNotThrow() =>
        // A loop that died still left the index alone, which is all the caller needs before disposing it.
        Assert.IsTrue(MonitorLoopJoin.Wait(Task.FromException(new InvalidOperationException("monitor blew up")), "C"));

    [TestMethod]
    public void CancelledLoop_CountsAsStoppedAndDoesNotThrow() =>
        Assert.IsTrue(MonitorLoopJoin.Wait(Task.FromCanceled(new CancellationToken(canceled: true)), "C"));

    [TestMethod]
    public void Stop_CancelsTheLoopAndReleasesTheTokenSourceOnceItEnds()
    {
        using var loopCts = new CancellationTokenSource();
        // Ends only because Stop cancels the token, the same way a monitor loop parked in Task.Delay does.
        var loop = Task.Delay(Timeout.InfiniteTimeSpan, loopCts.Token);

        Assert.IsTrue(MonitorLoopJoin.Stop(loop, loopCts, "C", TimeSpan.FromSeconds(5)));
        Assert.IsTrue(loopCts.IsCancellationRequested);

        // Released only after the loop returned: a source left alive would keep its registration on the
        // parent token, and one disposed too early would turn the loop's own winding-down waits into
        // ObjectDisposedException.
        Assert.ThrowsExactly<ObjectDisposedException>(() => loopCts.Cancel());
    }

    [TestMethod]
    public void Stop_WhenACancellationCallbackThrows_StillStopsTheLoop()
    {
        using var loopCts = new CancellationTokenSource();
        loopCts.Token.Register(() => throw new InvalidOperationException("callback blew up"));
        var loop = Task.Delay(Timeout.InfiniteTimeSpan, loopCts.Token);

        // Cancel() collects a throwing callback into an AggregateException; letting that out would hand an
        // exception back to the service-stop path this exists to keep alive, so it is logged and the stop
        // carries on.
        Assert.IsTrue(MonitorLoopJoin.Stop(loop, loopCts, "C", TimeSpan.FromSeconds(5)));
        Assert.IsTrue(loop.IsCompleted);
    }

    [TestMethod]
    public void Stop_WhenTheLoopRefusesToEnd_LeavesTheTokenSourceAlone()
    {
        using var loopCts = new CancellationTokenSource();
        var running = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            Assert.IsFalse(MonitorLoopJoin.Stop(running.Task, loopCts, "C", ShortTimeout));
            Assert.IsTrue(loopCts.IsCancellationRequested);

            // Not disposed: the loop may still be registering on this token while it unwinds.
            loopCts.Cancel();
        }
        finally
        {
            running.SetResult();
        }
    }
}
