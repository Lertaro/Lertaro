using Lertaro.Core.DriveMonitoring;

namespace Lertaro.Core.Tests.DriveMonitoring;

[TestClass]
public sealed class DriveMonitorRegistrationTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    [DataRow(true, true)]
    public async Task Dispose_WhileStopIsBlocked_AllCallersWaitAndObserveTheSameResult(bool fullDispose, bool stopThrows)
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var failure = new InvalidOperationException("stop failed");
        var monitor = new Disposable(() =>
        {
            entered.Set();
            release.Wait();
            if (stopThrows) throw failure;
        });
        var removal = new Disposable();
        var registration = new DriveMonitorRegistration(monitor, removal);
        var owner = Task.Run(() => Capture(fullDispose ? registration.Dispose : registration.DisposeMonitor));
        var waiters = new List<(Thread Thread, Task<Exception?> Result)>();
        try
        {
            Assert.IsTrue(entered.Wait(Deadline), "The underlying monitor never entered Dispose.");
            for (var i = 0; i < 2; i++)
            {
                var completion = new TaskCompletionSource<Exception?>(TaskCreationOptions.RunContinuationsAsynchronously);
                var thread = new Thread(() => completion.SetResult(Capture(registration.Dispose))) { IsBackground = true };
                waiters.Add((thread, completion.Task));
                thread.Start();
                Assert.IsTrue(SpinWait.SpinUntil(() => completion.Task.IsCompleted ||
                    (thread.ThreadState & ThreadState.WaitSleepJoin) != 0, Deadline), "Dispose never reached its wait.");
                Assert.IsFalse(completion.Task.IsCompleted, "Dispose returned before the shared stop finished.");
            }

            // Model the loop's final handle replacement. A stop waiter holding _gate would deadlock it.
            var lateRemoval = new Disposable();
            Assert.IsFalse(await Task.Run(() => registration.ReplaceRemovalMonitor(lateRemoval)).WaitAsync(Deadline));
            Assert.AreEqual(1, lateRemoval.Calls);
        }
        finally
        {
            release.Set();
            await owner.WaitAsync(Deadline);
            await Task.WhenAll(waiters.Select(w => w.Result)).WaitAsync(Deadline);
            foreach (var waiter in waiters)
                Assert.IsTrue(waiter.Thread.Join(Deadline));
        }

        Assert.AreSame(stopThrows ? failure : null, await owner);
        foreach (var waiter in waiters)
            Assert.AreSame(stopThrows ? failure : null, await waiter.Result);
        Assert.AreEqual(1, monitor.Calls);
        Assert.AreEqual(1, removal.Calls);
        if (stopThrows)
        {
            Assert.AreSame(failure, Assert.ThrowsExactly<InvalidOperationException>(registration.Dispose));
            Assert.AreSame(failure, Assert.ThrowsExactly<InvalidOperationException>(registration.DisposeMonitor));
        }
        else
        {
            registration.Dispose();
            registration.DisposeMonitor();
        }
        Assert.AreEqual(1, monitor.Calls);
        Assert.AreEqual(1, removal.Calls);
    }

    private static Exception? Capture(Action action)
    {
        try { action(); return null; }
        catch (Exception ex) { return ex; }
    }

    private sealed class Disposable(Action? dispose = null) : IDisposable
    {
        public int Calls;
        public void Dispose()
        {
            Interlocked.Increment(ref Calls);
            dispose?.Invoke();
        }
    }
}
