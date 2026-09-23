using System.Diagnostics;
using Lertaro.App.Services;

namespace Lertaro.App.Tests.Services;

// What the worker pool promises: shell work runs off the calling thread, on a reused apartment, and one
// bad action cannot take the pool down. Which folder a locate ends up showing is covered by
// ExplorerLocateHelperTests; the shell calls themselves need a live Explorer.
//
// The pool is process-wide static state these tests inspect, so they own it exclusively.
[TestClass]
[DoNotParallelize]
public sealed class ShellThreadTests
{
    // More than ShellThread.WorkerCount, so that round-robin posting reaches every worker at least twice.
    private const int Actions = 8;

    [TestMethod]
    public void Run_RunsTheActionOnAStaWorkerThatIsNotTheCaller()
    {
        using var done = new ManualResetEventSlim();
        Thread? ranOn = null;
        var apartment = ApartmentState.Unknown;

        ShellThread.Run("Test.Apartment", () =>
        {
            ranOn = Thread.CurrentThread;
            apartment = ranOn.GetApartmentState();
            done.Set();
        });

        Assert.IsTrue(done.Wait(TimeSpan.FromSeconds(5)), "the action never ran");
        Assert.IsNotNull(ranOn);
        Assert.AreNotEqual(Environment.CurrentManagedThreadId, ranOn!.ManagedThreadId, "ran inline on the caller");
        Assert.AreEqual(ApartmentState.STA, apartment, "shell work needs a single-threaded apartment");
        StringAssert.StartsWith(ranOn.Name, "ShellThread", $"unexpected worker name: {ranOn.Name}");
    }

    [TestMethod]
    public void Run_ReusesTheFixedPoolInsteadOfStartingAThreadPerAction()
    {
        // The reason the pool exists: Thread.Start waits for the new thread's loader initialisation on the
        // calling thread, so a thread per action is a UI-thread stall waiting to happen.
        using var done = new CountdownEvent(Actions);
        var threadIds = new System.Collections.Concurrent.ConcurrentBag<int>();

        for (var i = 0; i < Actions; i++)
            ShellThread.Run("Test.Reuse", () =>
            {
                threadIds.Add(Environment.CurrentManagedThreadId);
                done.Signal();
            });

        Assert.IsTrue(done.Wait(TimeSpan.FromSeconds(10)), "not every action ran");
        Assert.IsLessThanOrEqualTo(4, threadIds.Distinct().Count(),
            "more distinct workers than ShellThread.WorkerCount means a thread is being started per action");
    }

    [TestMethod]
    public void Run_ActionThatThrowsLeavesEveryWorkerUsable()
    {
        // Reaching every worker after the throw is what makes this meaningful: a worker that let the
        // exception escape to its own dispatcher would stop taking work, and the wait would time out.
        using var done = new CountdownEvent(Actions);

        ShellThread.Run("Test.Throws", () => throw new InvalidOperationException("boom"));
        for (var i = 0; i < Actions; i++)
            ShellThread.Run("Test.AfterThrow", () => done.Signal());

        Assert.IsTrue(done.Wait(TimeSpan.FromSeconds(10)),
            "a throwing action stopped a worker from taking further work");
    }

    [TestMethod]
    public void Run_ReturnsBeforeTheActionFinishes()
    {
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();

        var elapsed = Stopwatch.StartNew();
        ShellThread.Run("Test.Blocked", () =>
        {
            started.Set();
            release.Wait(TimeSpan.FromSeconds(10));
        });
        elapsed.Stop();
        release.Set();

        Assert.IsTrue(started.Wait(TimeSpan.FromSeconds(5)), "the action never started");
        Assert.IsTrue(elapsed.Elapsed < TimeSpan.FromSeconds(1),
            $"Run waited {elapsed.ElapsedMilliseconds}ms for the shell work instead of handing it off");
    }
}
