using Lertaro.App.ViewModels.Search;
using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Abstractions.Plugins;

namespace Lertaro.App.Tests.ViewModels.Search;

// A query token is contributed by a third-party plugin, so nothing that plugin does may take the host
// search down with it. A throw from CanHandle/ApplyAsync/GetHighlightText, a null list, or a list of
// results the host cannot render must each degrade to "this token narrowed nothing usable" rather than
// escaping into the search pipeline. Cancellation is the one signal that must travel outward, because
// the host relies on it to stop waiting for a query that has already been superseded.
[TestClass]
public sealed class QueryTokenDispatcherTests
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

        var result = await QueryTokenDispatcher.ApplyAsync(rows, []);

        Assert.AreSame(rows, result);
    }

    [TestMethod]
    public async Task ApplyAsync_UnclaimedToken_DropsEveryFileResult()
    {
        var result = await QueryTokenDispatcher.ApplyAsync(Rows(), ["nobody"], providers: []);

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

        var result = await QueryTokenDispatcher.ApplyAsync(Rows(), ["t"], providers: providers);

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

        var result = await QueryTokenDispatcher.ApplyAsync(Rows(), ["t"], providers: providers);

        Assert.IsEmpty(result);
    }

    [TestMethod]
    public async Task ApplyAsync_ApplyReturnsNull_DropsTheResults()
    {
        var providers = new IQueryTokenProvider[] { new StubProvider { ApplyReturns = null, ReturnsNull = true } };

        var result = await QueryTokenDispatcher.ApplyAsync(Rows(), ["t"], providers: providers);

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

        var result = await QueryTokenDispatcher.ApplyAsync(Rows(), ["t"], providers: providers);

        Assert.IsEmpty(result);
    }

    [TestMethod]
    public async Task ApplyAsync_HighlightThrows_KeepsTheResultsAndSkipsTheHighlight()
    {
        var providers = new IQueryTokenProvider[]
        {
            new StubProvider { HighlightThrows = new InvalidOperationException("boom"), Highlight = "beta" },
        };

        var result = await QueryTokenDispatcher.ApplyAsync(Rows(), ["t"], providers: providers);

        Assert.HasCount(2, result);
        Assert.AreEqual("alpha.txt", result[0].SearchQuery);
    }

    [TestMethod]
    public async Task ApplyAsync_HighlightText_IsAppendedToEverySearchQuery()
    {
        var providers = new IQueryTokenProvider[] { new StubProvider { Highlight = "beta" } };

        var result = await QueryTokenDispatcher.ApplyAsync(Rows(), ["t"], providers: providers);

        Assert.HasCount(2, result);
        Assert.AreEqual("alpha.txt beta", result[0].SearchQuery);
        Assert.AreEqual("beta.txt beta", result[1].SearchQuery);
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

        var result = await QueryTokenDispatcher.ApplyAsync(Rows(), ["reverse", "first"], providers: providers);

        // The "first" provider sees the reversed ordering, so it keeps "beta.txt".
        Assert.HasCount(1, result);
        Assert.AreEqual("beta.txt", result[0].Name);
    }

    [TestMethod]
    public async Task ApplyAsync_PassesTheCancellationTokenToTheProvider()
    {
        var provider = new StubProvider();
        using var cts = new CancellationTokenSource();

        await QueryTokenDispatcher.ApplyAsync(Rows(), ["t"], cts.Token, [provider]);

        Assert.AreEqual(cts.Token, provider.ObservedToken);
    }

    [TestMethod]
    public async Task ApplyAsync_CancelledToken_ThrowsBeforeCallingAnyProvider()
    {
        var provider = new StubProvider();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(
            () => QueryTokenDispatcher.ApplyAsync(Rows(), ["t"], cts.Token, [provider]));

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
            () => QueryTokenDispatcher.ApplyAsync(Rows(), ["t"], providers: providers));
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
