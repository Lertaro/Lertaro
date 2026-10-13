using System.Diagnostics;
using Lertaro.PluginSdk.Helpers;

namespace Lertaro.PluginSdk.Tests.Helpers;

// What this batch exists for is the budget and the parallelism, so the probes are handed in rather than
// being real files: a real dead network share is neither something a test can set up nor something it
// should wait on.
//
// Runs on its own: several of these hold a batch of probes on purpose, and this assembly parallelizes per
// test method, so a parked batch must not be able to slow an unrelated test's thread pool down.
[TestClass]
[DoNotParallelize]
public sealed class BudgetedProbeBatchTests
{
    // Long enough that a correct implementation never comes near it.
    private static readonly TimeSpan GenerousBudget = TimeSpan.FromSeconds(30);

    [TestMethod]
    public void Run_EmptyBatch_IsEmpty()
        => Assert.IsEmpty(BudgetedProbeBatch.Run(Array.Empty<Func<bool>>(), false, GenerousBudget));

    [TestMethod]
    public void Run_KeepsEveryAnswerThatArrives()
    {
        var answers = BudgetedProbeBatch.Run(
            new Func<int>[] { () => 1, () => 2, () => 3 }, unanswered: -1, GenerousBudget);

        CollectionAssert.AreEqual(new[] { 1, 2, 3 }, answers);
    }

    [TestMethod]
    public void Run_ProbeThatThrows_CountsAsUnanswered()
    {
        // A probe that throws is a path that could not be asked about at all, which is the same answer as
        // one that is not there.
        var answers = BudgetedProbeBatch.Run(
            new Func<bool>[] { () => true, () => throw new IOException("share went away"), () => true },
            unanswered: false, GenerousBudget);

        CollectionAssert.AreEqual(new[] { true, false, true }, answers);
    }

    [TestMethod]
    public void Run_ProbeStillBlockedAtTheBudget_CountsAsUnansweredAndEndsTheWait()
    {
        using var released = new ManualResetEventSlim(false);
        try
        {
            var elapsed = Stopwatch.StartNew();
            var answers = BudgetedProbeBatch.Run(
                new Func<bool>[] { () => { released.Wait(); return true; } },
                unanswered: false,
                TimeSpan.FromMilliseconds(200));

            Assert.IsFalse(answers[0], "a probe that never answered is not there");
            Assert.IsTrue(elapsed.Elapsed < TimeSpan.FromSeconds(10),
                $"the budget is what ends the wait, took {elapsed.Elapsed}");
        }
        finally
        {
            released.Set();
        }
    }

    // The reason the batch is parallel at all: the probes that do answer must not queue behind one that does
    // not. Every probe below blocks until all of them have started, so they can only all start at once.
    [TestMethod]
    public async Task Run_DoesNotQueueProbesBehindEachOther()
    {
        const int ProbeCount = 4;
        using var allStarted = new CountdownEvent(ProbeCount);
        using var release = new ManualResetEventSlim(false);

        var probes = Enumerable.Range(0, ProbeCount)
            .Select(_ => (Func<bool>)(() =>
            {
                allStarted.Signal();
                release.Wait();
                return true;
            }))
            .ToList();

        var run = Task.Run(() => BudgetedProbeBatch.Run(probes, unanswered: false, GenerousBudget));
        bool started;
        try
        {
            started = allStarted.Wait(TimeSpan.FromSeconds(15));
        }
        finally
        {
            // Set either way, so a serial implementation still lets its probes go instead of leaving their
            // threads parked on this event after the test has already failed.
            release.Set();
        }

        Assert.IsTrue(started, "every probe has to be running before any of them answers");
        CollectionAssert.AreEqual(new[] { true, true, true, true }, await run);
    }

    // A batch wider than the worker count still has to be worked through, not just the first count's worth.
    [TestMethod]
    public void Run_MoreProbesThanTheCap_AnswersAllOfThem()
    {
        const int ProbeCount = 20;
        using var release = new ManualResetEventSlim(false);

        var probes = Enumerable.Range(0, ProbeCount)
            .Select(index => (Func<int>)(() =>
            {
                release.Wait();
                return index;
            }))
            .ToList();

        var run = Task.Run(() => BudgetedProbeBatch.Run(probes, unanswered: -1, TimeSpan.FromSeconds(20)));
        // Long enough for the batch to have taken up whatever workers it has. Releasing earlier only makes
        // this weaker (fewer probes were actually held), never flakier.
        Thread.Sleep(500);
        release.Set();

        CollectionAssert.AreEqual(Enumerable.Range(0, ProbeCount).ToArray(), run.GetAwaiter().GetResult());
    }
}
