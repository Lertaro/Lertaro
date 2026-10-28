using Lertaro.Plugins.CoreExtensions.Preview.Handlers;

namespace Lertaro.Plugins.CoreExtensions.Tests.Preview.Handlers;

// The whole of 乙 rests on three properties of this one apartment: work leaves the calling thread, it runs in an STA
// that a window-creating preview handler can use, and a single long call cannot break what follows it. Each is pinned
// here rather than inferred, because the failure mode they prevent (#459: Word's import prompt holding the WPF UI
// thread for 17 seconds) only shows up on a machine with Office installed.
[STATestClass]
public sealed class PreviewStaWorkerTests
{
    [STATestMethod]
    public void Post_RunsInItsOwnStaThreadNotTheCallers()
    {
        var callerThread = Environment.CurrentManagedThreadId;
        int? workerThread = null;
        var workerApartment = ApartmentState.Unknown;
        using var done = new ManualResetEventSlim();

        PreviewStaWorker.Post(() =>
        {
            workerThread = Environment.CurrentManagedThreadId;
            workerApartment = Thread.CurrentThread.GetApartmentState();
            done.Set();
        });

        Assert.IsTrue(done.Wait(5000), "the posted work never ran");
        Assert.IsNotNull(workerThread);
        Assert.AreNotEqual(callerThread, workerThread);
        Assert.AreEqual(ApartmentState.STA, workerApartment);
    }

    [STATestMethod]
    public void Post_RunsOneCallAtATimeInOrderEvenWhileOneBlocks()
    {
        var order = new List<string>();
        using var slowStarted = new ManualResetEventSlim();
        using var slowFinished = new ManualResetEventSlim();
        using var secondDone = new ManualResetEventSlim();

        PreviewStaWorker.Post(() =>
        {
            lock (order) order.Add("slow");
            slowStarted.Set();
            slowFinished.Wait(2000);
        });
        Assert.IsTrue(slowStarted.Wait(5000), "the first item never started on the apartment");

        // Queued while the first is still inside its call, exactly as a second preview would be during an Office
        // cold start. It must not start until the running one returns -- that ordering is what makes a teardown
        // posted during a render safe without any extra locking.
        PreviewStaWorker.Post(() =>
        {
            lock (order) order.Add("second");
            secondDone.Set();
        });
        Assert.IsFalse(secondDone.Wait(150), "the queued item ran while another was still in flight");

        slowFinished.Set();
        Assert.IsTrue(secondDone.Wait(5000), "the queued item never ran after the first returned");
        CollectionAssert.AreEqual(new List<string> { "slow", "second" }, order);
    }

    [STATestMethod]
    public void Post_KeepsTheApartmentAliveWhenWorkThrows()
    {
        using var afterThrow = new ManualResetEventSlim();

        PreviewStaWorker.Post(() => throw new InvalidOperationException("handler blew up"));
        PreviewStaWorker.Post(() => afterThrow.Set());

        // An unhandled exception on the apartment's thread would end it, and every later preview would fall back to
        // running inline on the UI thread -- the freeze this class exists to prevent, arriving as a silent regression.
        Assert.IsTrue(afterThrow.Wait(5000), "the apartment did not survive the throwing item");
    }
}
