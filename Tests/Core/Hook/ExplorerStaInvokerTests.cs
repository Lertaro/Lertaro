using Lertaro.Core.Hook;

namespace Lertaro.Core.Tests.Hook;

[TestClass]
public sealed class ExplorerStaInvokerTests
{
    [TestMethod]
    public void RunOnStaWithTimeout_Completed_ReturnsResultAndNotTimedOut()
    {
        var result = ExplorerStaInvoker.RunOnStaWithTimeout(() => "ok", "fallback", TimeSpan.FromSeconds(1), out var timedOut);

        Assert.AreEqual("ok", result);
        Assert.IsFalse(timedOut);
    }

    [TestMethod]
    public void RunOnStaWithTimeout_TimedOut_ReturnsFallbackAndSetsTimedOut()
    {
        var workerGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var result = ExplorerStaInvoker.RunOnStaWithTimeout(() =>
        {
            workerGate.Task.GetAwaiter().GetResult();
            return "late";
        }, "fallback", TimeSpan.FromMilliseconds(20), out var timedOut);

        Assert.AreEqual("fallback", result);
        Assert.IsTrue(timedOut);

        // Release the abandoned STA thread so it can finish and clean up its budget/event.
        workerGate.SetResult(true);
        Thread.Sleep(50);
    }
}
