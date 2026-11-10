using Lertaro.App.ViewModels.Search;
using Lertaro.App.ViewModels.Search.Dispatch;
using Lertaro.App.Services.Plugin;
using Lertaro.Core.Services.Search;
using System.Reflection;
using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Abstractions.Plugins;

namespace Lertaro.App.Tests.ViewModels.Search;

[TestClass]
[DoNotParallelize]
public sealed class QueryTokenDispatcherTests
{
    [STATestMethod]
    public async Task QuickController_LateOlderSnapshot_DoesNotOverwriteTheFinalSnapshot()
    {
        using var vm = new QuickSearchViewModel();
        using var service = new SearchService();
        using var engine = new SearchExecutionEngine(service);
        // Supply a live query lifetime without starting IPC, then exercise the real composition entry.
        typeof(SearchExecutionEngine).GetField("_searchCts", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(engine, new CancellationTokenSource());
        var renders = new List<List<AppSearchResult>>();
        var controller = new SearchDispatchController(engine, vm, () => null, () => false, () => "report :audit",
            _ => { }, _ => { }, _ => { }, rows => renders.Add(rows.ToList()), () => 0);
        IReadOnlyList<string> tokens = ["audit"];
        typeof(SearchDispatchController).GetField("_queryTokens", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(controller, tokens);
        var pending = new TaskCompletionSource<IReadOnlyList<ISearchResult>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var provider = new TokenProvider((_, rows) => ++calls == 1 ? pending.Task : Task.FromResult(rows));
        var providers = (List<IQueryTokenProvider>)typeof(PluginManager)
            .GetField("_queryTokenProviders", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(PluginManager.Instance)!;
        providers.Insert(0, provider);
        var method = typeof(SearchDispatchController).GetMethod("ComposeAndApplyGuardedAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        List<AppSearchResult> earlier = [new() { Name = "a", FullPath = @"Z:\a.txt", ResultKind = "File" }];
        List<AppSearchResult> final = [.. earlier, new() { Name = "b", FullPath = @"Z:\b.txt", ResultKind = "File" }];
        try
        {
            var older = (Task)method.Invoke(controller, ["report :audit", "report", earlier, tokens, "old", false, null])!;
            var newer = (Task)method.Invoke(controller, ["report :audit", "report", final, tokens, "final", true, null])!;
            await newer;
            pending.SetResult(earlier);
            await older;
            CollectionAssert.AreEqual(final, Assert.ContainsSingle(renders));
            Assert.AreEqual("final", vm.Monitor.StatusText);
        }
        finally { providers.Remove(provider); }
    }

    [TestMethod]
    public async Task ApplyAsync_RepeatedSharedRows_HighlightsFromTheBaseQuery()
    {
        var row = new AppSearchResult { Name = "report", FullPath = @"Z:\report.txt", SearchQuery = "report" };
        var provider = new TokenProvider((_, rows) => Task.FromResult(rows));
        var first = await QueryTokenDispatcher.ApplyAsync([row], ["path"], "report", providers: [provider]);
        var second = await QueryTokenDispatcher.ApplyAsync(first, ["path"], "report", providers: [provider]);
        Assert.AreSame(row, Assert.ContainsSingle(second));
        Assert.AreEqual("report archive", row.SearchQuery);
        await QueryTokenDispatcher.ApplyAsync(second, ["sort"], "report", providers: [provider]);
        Assert.AreEqual("report", row.SearchQuery);
    }

    [TestMethod]
    public async Task ApplyAsync_CancelledOlderBatchCannotMutateSharedRowsAfterNewerBatch()
    {
        var row = new AppSearchResult { Name = "report", FullPath = @"Z:\report.txt", SearchQuery = "report" };
        var pending = new TaskCompletionSource<IReadOnlyList<ISearchResult>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var provider = new TokenProvider((_, rows) => ++calls == 1 ? pending.Task : Task.FromResult(rows));
        using var cancelled = new CancellationTokenSource();
        var older = QueryTokenDispatcher.ApplyAsync([row], ["path", "sort"], "old", cancelled.Token, [provider]);
        cancelled.Cancel();
        await QueryTokenDispatcher.ApplyAsync([row], ["path"], "new", providers: [provider]);
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await older);
        pending.SetResult([row]);
        Assert.AreEqual("new archive", row.SearchQuery);
        Assert.AreEqual(2, calls, "Cancellation must prevent the remaining token chain from starting.");
    }

    private sealed class TokenProvider(Func<string, IReadOnlyList<ISearchResult>, Task<IReadOnlyList<ISearchResult>>> apply) : IQueryTokenProvider
    {
        public string Name => "Test token";
        public bool CanHandle(string token) => true;
        public Task<IReadOnlyList<ISearchResult>> ApplyAsync(string token, IReadOnlyList<ISearchResult> results) => apply(token, results);
        public string? GetHighlightText(string token) => token == "path" ? "archive" : null;
    }
}
