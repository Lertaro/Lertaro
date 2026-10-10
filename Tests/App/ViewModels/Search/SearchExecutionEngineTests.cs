using System.Windows.Threading;
using Lertaro.App.ViewModels.Search;

namespace Lertaro.App.Tests.ViewModels.Search;

[TestClass]
public sealed class SearchExecutionEngineTests
{
    [STATestMethod]
    public async Task RunDebouncedSearchAsync_ReadySearch_LetsQueuedInputRunFirst()
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        var calls = new List<string>();
        var searchedOnDispatcher = false;
        var search = SearchExecutionEngine.RunDebouncedSearchAsync(Task.CompletedTask, dispatcher, () =>
        {
            searchedOnDispatcher = dispatcher.CheckAccess();
            calls.Add("search");
        }, CancellationToken.None);
        Assert.IsFalse(search.IsCompleted);
        _ = dispatcher.BeginInvoke(() => calls.Add("input"), DispatcherPriority.Input);

        DrainDispatcher(dispatcher);
        await search;

        CollectionAssert.AreEqual(new[] { "input", "search" }, calls);
        Assert.IsTrue(searchedOnDispatcher);
    }

    [STATestMethod]
    public async Task RunDebouncedSearchAsync_SupersededAfterDelay_OnlyStartsNewSearch()
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        using var oldQuery = new CancellationTokenSource();
        var calls = new List<string>();
        var oldSearch = SearchExecutionEngine.RunDebouncedSearchAsync(Task.CompletedTask, dispatcher,
            () => calls.Add("old"), oldQuery.Token);
        Assert.IsFalse(oldSearch.IsCompleted);

        // Reproduce typing again after the timer fired but before the UI queue caught up.
        oldQuery.Cancel();
        var newSearch = SearchExecutionEngine.RunDebouncedSearchAsync(Task.CompletedTask, dispatcher,
            () => calls.Add("new"), CancellationToken.None);
        DrainDispatcher(dispatcher);
        await Task.WhenAll(oldSearch, newSearch);

        CollectionAssert.AreEqual(new[] { "new" }, calls);
    }

    [STATestMethod]
    public async Task RunDebouncedSearchAsync_CancelledWhileWaiting_DoesNotStartSearch()
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        using var query = new CancellationTokenSource();
        var delay = new TaskCompletionSource();
        var calls = 0;
        var search = SearchExecutionEngine.RunDebouncedSearchAsync(delay.Task, dispatcher, () => calls++, query.Token);

        DrainDispatcher(dispatcher);
        Assert.IsFalse(search.IsCompleted);
        Assert.AreEqual(0, calls);
        query.Cancel();
        delay.SetCanceled(query.Token);
        await search;

        Assert.AreEqual(0, calls);
    }

    [STATestMethod]
    public async Task RunDebouncedSearchAsync_CancelledBeforeDispatchAndSourceDisposed_DoesNotStartSearch()
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        var query = new CancellationTokenSource();
        var calls = 0;
        var search = SearchExecutionEngine.RunDebouncedSearchAsync(Task.CompletedTask, dispatcher, () => calls++, query.Token);

        query.Cancel();
        query.Dispose();
        DrainDispatcher(dispatcher);
        await search;

        Assert.AreEqual(0, calls);
    }

    private static void DrainDispatcher(Dispatcher dispatcher)
    {
        var frame = new DispatcherFrame();
        dispatcher.BeginInvoke(() => frame.Continue = false, DispatcherPriority.ApplicationIdle);
        Dispatcher.PushFrame(frame);
    }
}
