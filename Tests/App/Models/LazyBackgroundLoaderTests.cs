namespace Lertaro.App.Tests.Models;

// The bounded permit wait is what stops a queue of icon loads from growing without limit when the loads
// ahead of it are wedged -- and it has to say so, because AppSearchResultDisplaySupport only re-offers a
// row for loading once its one-way started latch is cleared.
[TestClass]
[DoNotParallelize]
public sealed class LazyBackgroundLoaderTests
{
    [TestMethod]
    public async Task Start_PermitAvailable_RunsTheLoadAndGivesThePermitBack()
    {
        using var semaphore = new SemaphoreSlim(1);
        var ran = false;

        await LazyBackgroundLoader.Start(semaphore, () =>
        {
            ran = true;
            return Task.CompletedTask;
        });

        Assert.IsTrue(ran);
        Assert.AreEqual(1, semaphore.CurrentCount);
    }

    [TestMethod]
    public async Task Start_PermitNotFreedInTime_SkipsTheLoadAndReportsIt()
    {
        using var semaphore = new SemaphoreSlim(1);
        await semaphore.WaitAsync();
        var ran = false;
        var reported = false;

        await LazyBackgroundLoader.Start(semaphore,
                                         () =>
                                         {
                                             ran = true;
                                             return Task.CompletedTask;
                                         },
                                         onWaitTimedOut: () => reported = true,
                                         waitTimeoutMs: 50);

        Assert.IsTrue(reported);
        Assert.IsFalse(ran, "a load that never got a permit must not run half-started behind the queue");
        Assert.AreEqual(0, semaphore.CurrentCount, "skipping the wait may not hand out a permit it never took");
    }

    [TestMethod]
    public async Task Start_LoadThrows_StillReleasesThePermit()
    {
        using var semaphore = new SemaphoreSlim(1);

        await LazyBackgroundLoader.Start(semaphore, () => throw new InvalidOperationException("wedged load"));

        Assert.AreEqual(1, semaphore.CurrentCount, "an exception inside the load may not strand the permit");
    }
}
