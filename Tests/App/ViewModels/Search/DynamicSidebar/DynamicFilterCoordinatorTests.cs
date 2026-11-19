using System.Windows.Threading;
using Lertaro.App.ViewModels.Search.DynamicSidebar;

namespace Lertaro.App.Tests.ViewModels.Search.DynamicSidebar;

[TestClass]
public sealed class DynamicFilterCoordinatorTests
{
    private static AppSearchResult Result(string path) => new() { FullPath = path, Name = path };
    private static Task<List<AppSearchResult>> IdentitySort(IEnumerable<AppSearchResult> rows, CancellationToken token) =>
        Task.FromResult(rows.ToList());

    [TestMethod]
    public async Task Apply_OnlySortsAndRendersTheFilteredAnswer()
    {
        var coordinator = new DynamicFilterCoordinator();
        List<AppSearchResult> all = [Result("a"), Result("b")];
        var renders = new List<List<AppSearchResult>>();
        var busy = new List<bool>();
        var sortedSizes = new List<int>();

        await coordinator.Apply(all, [row => row.FullPath == "a"],
            (rows, token) => { var list = rows.ToList(); sortedSizes.Add(list.Count); return Task.FromResult(list); },
            () => all, renders.Add, busy.Add);

        Assert.AreEqual("a", Assert.ContainsSingle(Assert.ContainsSingle(renders)).FullPath);
        Assert.AreEqual(1, Assert.ContainsSingle(sortedSizes));
        CollectionAssert.AreEqual(new[] { true, false }, busy);
    }

    [TestMethod]
    public async Task Apply_NewerProjectionWinsWhileOldPredicateIsBlocked()
    {
        var coordinator = new DynamicFilterCoordinator();
        List<AppSearchResult> current = [Result("old")];
        var renders = new List<List<AppSearchResult>>();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var old = coordinator.Apply(current, [_ =>
        {
            entered.SetResult();
            if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException();
            return true;
        }], IdentitySort, () => current, renders.Add, _ => { });
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            current = [Result("new")];
            await coordinator.Apply(current, [], IdentitySort, () => current, renders.Add, _ => { });
        }
        finally { release.Set(); await old; }

        Assert.AreEqual("new", Assert.ContainsSingle(Assert.ContainsSingle(renders)).FullPath);
    }

    [TestMethod]
    public async Task Cancel_StopsWalkingOldRowsBeforeTheNewQueryHasResults()
    {
        var coordinator = new DynamicFilterCoordinator();
        var all = Enumerable.Range(0, 10_000).Select(i => Result(i.ToString())).ToList();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var visits = 0;
        var rendered = false;
        var old = coordinator.Apply(all, [_ =>
        {
            Interlocked.Increment(ref visits);
            entered.TrySetResult();
            if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException();
            return true;
        }], IdentitySort, () => all, _ => rendered = true, _ => { });
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            coordinator.Cancel();
        }
        finally { release.Set(); await old; }

        Assert.AreEqual(1, visits);
        Assert.IsFalse(rendered);
    }

    [STATestMethod]
    public void Apply_ComputesOnWorkerAndPublishesOnTheCallingDispatcher()
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
        try
        {
            var caller = Environment.CurrentManagedThreadId;
            var worker = caller;
            var publisher = -1;
            List<AppSearchResult> all = [Result("a")];
            var coordinator = new DynamicFilterCoordinator();
            var work = coordinator.Apply(all, [_ => { worker = Environment.CurrentManagedThreadId; return true; }],
                IdentitySort, () => all, _ => publisher = Environment.CurrentManagedThreadId, _ => { });
            var frame = new DispatcherFrame();
            _ = work.ContinueWith(_ => dispatcher.BeginInvoke(() => frame.Continue = false), TaskScheduler.Default);
            Dispatcher.PushFrame(frame);
            work.GetAwaiter().GetResult();

            Assert.AreNotEqual(caller, worker);
            Assert.AreEqual(caller, publisher);
        }
        finally { SynchronizationContext.SetSynchronizationContext(previousContext); }
    }
}
