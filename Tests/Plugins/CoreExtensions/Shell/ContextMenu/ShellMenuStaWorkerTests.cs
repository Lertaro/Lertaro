using System.Windows.Threading;
using Lertaro.Plugins.CoreExtensions.Shell.ContextMenu;

namespace Lertaro.Plugins.CoreExtensions.Tests.Shell.ContextMenu;

// What the pool promises: a fixed set of STA apartments started before the first shell extension runs,
// reused by later sessions, and never grown -- growing means starting a thread after a wedged extension
// may hold the loader lock, which is exactly the freeze this replaces. Building the menu itself needs a
// live Explorer/COM.
//
// The pool is process-wide static state these tests inspect, so they own it exclusively.
[TestClass]
[DoNotParallelize]
public sealed class ShellMenuStaWorkerTests
{
    private readonly List<Dispatcher> _acquired = new();

    private Dispatcher? Acquire()
    {
        var worker = ShellMenuStaWorker.Acquire();
        if (worker != null) _acquired.Add(worker);
        return worker;
    }

    private List<Dispatcher> AcquireAll()
    {
        var workers = new List<Dispatcher>();
        for (var i = 0; i < ShellMenuStaWorker.WorkerCount; i++)
        {
            var worker = Acquire();
            Assert.IsNotNull(worker, $"worker {i + 1} of {ShellMenuStaWorker.WorkerCount} was not available");
            workers.Add(worker);
        }
        return workers;
    }

    private void Release(Dispatcher worker)
    {
        ShellMenuStaWorker.Release(worker);
        _acquired.Remove(worker);
    }

    [TestCleanup]
    public void ReleaseAcquiredWorkers()
    {
        foreach (var worker in _acquired)
            ShellMenuStaWorker.Release(worker);
        _acquired.Clear();
    }

    [TestMethod]
    public void Acquire_ReturnsTheWholePoolAsDistinctStaApartments_ThenNullWhenExhausted()
    {
        var workers = AcquireAll();

        Assert.AreEqual(ShellMenuStaWorker.WorkerCount,
            workers.Select(w => w.Thread.ManagedThreadId).Distinct().Count(),
            "each pool slot must be its own apartment");
        Assert.IsTrue(workers.All(w => w.Thread.GetApartmentState() == ApartmentState.STA),
            "shell context menus need single-threaded apartments");
        Assert.IsNull(Acquire(), "the pool must never grow past its fixed size");
    }

    [TestMethod]
    public void Release_HandsTheApartmentBackToThePool_WithoutGrowingIt()
    {
        var workers = AcquireAll();
        Release(workers[0]);

        var reacquired = Acquire();
        Assert.IsNotNull(reacquired);
        CollectionAssert.Contains(workers, reacquired, "a later session must reuse one of the started apartments");
        Assert.IsNull(Acquire(), "reusing a released apartment must not add a slot");
    }
}
