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
    [TestMethod]
    public void InvalidTokenHint_OnlyReportsUnclaimedTokens()
    {
        var provider = new TokenProvider((_, rows) => Task.FromResult(rows));
        Assert.IsNull(QueryTokenDispatcher.InvalidTokenHint(["sort"], [provider]));
        Assert.IsNotNull(QueryTokenDispatcher.InvalidTokenHint(["<s>garbage"], []));
        Assert.IsNull(QueryTokenDispatcher.InvalidTokenHint([], []));
    }
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

// A query token is contributed by a third-party plugin, so nothing that plugin does may take the host
// search down with it. A throw from CanHandle/ApplyAsync/GetHighlightText, a null list, or a list of
// results the host cannot render must each degrade to "this token narrowed nothing usable" rather than
// escaping into the search pipeline. Cancellation is the one signal that must travel outward, because
// the host relies on it to stop waiting for a query that has already been superseded.
[TestClass]
public sealed class QueryTokenDispatcherFailureTests
{
    private static List<AppSearchResult> Rows() =>
    [
        new AppSearchResult { Name = "alpha.txt", FullPath = @"T:\alpha.txt", SearchQuery = "alpha.txt" },
        new AppSearchResult { Name = "beta.txt", FullPath = @"T:\beta.txt", SearchQuery = "beta.txt" },
    ];

    [TestMethod]
    public async Task ApplyAsync_NoTokens_ReturnsTheSameListInstance()
    {
        var rows = Rows();

        var result = await QueryTokenDispatcher.ApplyAsync(rows, [], "query");

        Assert.AreSame(rows, result);
    }

    [TestMethod]
    public async Task ApplyAsync_UnclaimedToken_DropsEveryFileResult()
    {
        var result = await QueryTokenDispatcher.ApplyAsync(Rows(), ["nobody"], "query", providers: []);

        Assert.IsEmpty(result);
    }

    [TestMethod]
    public async Task ApplyAsync_CanHandleThrows_IsIsolatedAndALaterProviderStillClaims()
    {
        var providers = new IQueryTokenProvider[]
        {
            new StubProvider { CanHandleThrows = new InvalidOperationException("boom") },
            new StubProvider { Transform = rows => [rows[1]] },
        };

        var result = await QueryTokenDispatcher.ApplyAsync(Rows(), ["t"], "query", providers: providers);

        Assert.HasCount(1, result);
        Assert.AreEqual("beta.txt", result[0].Name);
    }

    [TestMethod]
    public async Task ApplyAsync_ApplyThrows_DropsTheResultsInsteadOfPropagating()
    {
        var providers = new IQueryTokenProvider[]
        {
            new StubProvider { ApplyThrows = new InvalidOperationException("boom") },
        };

        var result = await QueryTokenDispatcher.ApplyAsync(Rows(), ["t"], "query", providers: providers);

        Assert.IsEmpty(result);
    }

    [TestMethod]
    public async Task ApplyAsync_ApplyReturnsNull_DropsTheResults()
    {
        var providers = new IQueryTokenProvider[] { new StubProvider { ApplyReturns = null, ReturnsNull = true } };

        var result = await QueryTokenDispatcher.ApplyAsync(Rows(), ["t"], "query", providers: providers);

        Assert.IsEmpty(result);
    }

    [TestMethod]
    public async Task ApplyAsync_ApplyReturnsForeignResults_DropsTheResults()
    {
        // The host renders AppSearchResult rows only, so a plugin handing back its own ISearchResult
        // implementation would fail later, deep in the UI, if it were accepted here.
        var providers = new IQueryTokenProvider[]
        {
            new StubProvider { ApplyReturns = [new ForeignResult()] },
        };

        var result = await QueryTokenDispatcher.ApplyAsync(Rows(), ["t"], "query", providers: providers);

        Assert.IsEmpty(result);
    }

    [TestMethod]
    public async Task ApplyAsync_HighlightThrows_KeepsTheResultsAndSkipsTheHighlight()
    {
        var providers = new IQueryTokenProvider[]
        {
            new StubProvider { HighlightThrows = new InvalidOperationException("boom"), Highlight = "beta" },
        };

        var result = await QueryTokenDispatcher.ApplyAsync(Rows(), ["t"], "query", providers: providers);

        Assert.HasCount(2, result);
        Assert.AreEqual("query", result[0].SearchQuery);
    }

    [TestMethod]
    public async Task ApplyAsync_HighlightText_IsAppendedToEverySearchQuery()
    {
        var providers = new IQueryTokenProvider[] { new StubProvider { Highlight = "beta" } };

        var result = await QueryTokenDispatcher.ApplyAsync(Rows(), ["t"], "query", providers: providers);

        Assert.HasCount(2, result);
        Assert.AreEqual("query beta", result[0].SearchQuery);
        Assert.AreEqual("query beta", result[1].SearchQuery);
    }

    [TestMethod]
    public async Task ApplyAsync_TwoTokens_ChainThroughTheirOwnProvidersInTokenOrder()
    {
        // Each token is re-offered to the providers from the start, so the two stubs have to claim
        // distinct tokens for this to exercise the chain rather than one provider running twice.
        var providers = new IQueryTokenProvider[]
        {
            new StubProvider { ClaimsToken = "reverse", Transform = rows => [rows[1], rows[0]] },
            new StubProvider { ClaimsToken = "first", Transform = rows => [rows[0]] },
        };

        var result = await QueryTokenDispatcher.ApplyAsync(Rows(), ["reverse", "first"], "query", providers: providers);

        // The "first" provider sees the reversed ordering, so it keeps "beta.txt".
        Assert.HasCount(1, result);
        Assert.AreEqual("beta.txt", result[0].Name);
    }

    [TestMethod]
    public async Task ApplyAsync_PassesTheCancellationTokenToTheProvider()
    {
        var provider = new StubProvider();
        using var cts = new CancellationTokenSource();

        await QueryTokenDispatcher.ApplyAsync(Rows(), ["t"], "query", cts.Token, [provider]);

        Assert.AreEqual(cts.Token, provider.ObservedToken);
    }

    [TestMethod]
    public async Task ApplyAsync_CancelledToken_ThrowsBeforeCallingAnyProvider()
    {
        var provider = new StubProvider();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(
            () => QueryTokenDispatcher.ApplyAsync(Rows(), ["t"], "query", cts.Token, [provider]));

        Assert.IsNull(provider.ObservedToken);
    }

    [TestMethod]
    public async Task ApplyAsync_ProviderCancels_PropagatesTheCancellation()
    {
        // A provider may surface the host's cancellation itself; swallowing it would leave the host
        // waiting on a query that can never finish.
        var providers = new IQueryTokenProvider[]
        {
            new StubProvider { ApplyThrows = new OperationCanceledException() },
        };

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(
            () => QueryTokenDispatcher.ApplyAsync(Rows(), ["t"], "query", providers: providers));
    }

    // One configurable stand-in for a plugin-contributed provider: every failure mode the dispatcher has
    // to survive is expressed as data rather than as another subclass.
    private sealed class StubProvider : IQueryTokenProvider
    {
        public Exception? CanHandleThrows { get; init; }
        public Exception? ApplyThrows { get; init; }
        public Exception? HighlightThrows { get; init; }
        public bool ReturnsNull { get; init; }
        public string? ClaimsToken { get; init; }
        public Func<IReadOnlyList<ISearchResult>, IReadOnlyList<ISearchResult>>? Transform { get; init; }
        public IReadOnlyList<ISearchResult>? ApplyReturns { get; init; }
        public string? Highlight { get; init; }
        public CancellationToken? ObservedToken { get; private set; }

        public bool CanHandle(string token) => CanHandleThrows != null
            ? throw CanHandleThrows
            : ClaimsToken == null || ClaimsToken == token;

        public Task<IReadOnlyList<ISearchResult>> ApplyAsync(string token, IReadOnlyList<ISearchResult> results)
            => Task.FromResult(ApplyNow(results));

        public Task<IReadOnlyList<ISearchResult>> ApplyAsync(string token, IReadOnlyList<ISearchResult> results, CancellationToken cancellationToken)
        {
            ObservedToken = cancellationToken;
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(ApplyNow(results));
        }

        public string? GetHighlightText(string token) => HighlightThrows != null ? throw HighlightThrows : Highlight;

        private IReadOnlyList<ISearchResult> ApplyNow(IReadOnlyList<ISearchResult> results)
        {
            if (ApplyThrows != null)
                throw ApplyThrows;

            if (ReturnsNull)
                return null!;

            return ApplyReturns ?? Transform?.Invoke(results) ?? results;
        }
    }

    private sealed class ForeignResult : ISearchResult
    {
        public string Name => "foreign";
        public string FullPath => @"T:\foreign";
        public string ContextDirectory => @"T:\";
        public bool IsDir => false;
        public bool IsApplication => false;
    }
}
