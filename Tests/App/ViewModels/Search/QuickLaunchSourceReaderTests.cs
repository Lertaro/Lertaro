using System.IO;
using Lertaro.App.Services.Plugin;
using Lertaro.App.ViewModels.Search;
using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Abstractions.Plugins;

namespace Lertaro.App.Tests.ViewModels.Search;

// Asking a provider is the expensive half of a summon -- a folder read, a COM resolve per entry, an IPC
// round trip -- so what is tested here is exactly when it gets asked again. Shares the cache's own static
// state, hence the same run-on-its-own marker.
[TestClass]
[DoNotParallelize]
public sealed class QuickLaunchSourceReaderTests
{
    [TestInitialize]
    public void Reset() => QuickLaunchSourceCache.Invalidate();

    [TestCleanup]
    public void Cleanup() => QuickLaunchSourceCache.Invalidate();

    [TestMethod]
    public async Task LoadProviderAsync_SecondAskWithinTheLifetime_DoesNotAskTheProviderAgain()
    {
        var provider = new CountingProvider(Results(@"C:\one"));

        await QuickLaunchSourceReader.LoadProviderAsync("source", provider, CancellationToken.None);
        var again = await QuickLaunchSourceReader.LoadProviderAsync("source", provider, CancellationToken.None);

        Assert.AreEqual(1, provider.Calls);
        Assert.AreEqual(@"C:\one", again.Single().FullPath);
    }

    [TestMethod]
    public async Task LoadProviderAsync_SourcesAreRememberedApartFromEachOther()
    {
        var first = new CountingProvider(Results(@"C:\one"));
        var second = new CountingProvider(Results(@"C:\two"));

        await QuickLaunchSourceReader.LoadProviderAsync("first", first, CancellationToken.None);
        await QuickLaunchSourceReader.LoadProviderAsync("second", second, CancellationToken.None);
        var again = await QuickLaunchSourceReader.LoadProviderAsync("second", second, CancellationToken.None);

        Assert.AreEqual(@"C:\two", again.Single().FullPath);
        Assert.AreEqual(1, first.Calls);
        Assert.AreEqual(1, second.Calls);
    }

    // A source can legitimately answer nothing before the service it needs is up, so nothing is the one
    // answer that must never be kept: remembering it would keep the tab missing for the whole lifetime.
    [TestMethod]
    public async Task LoadProviderAsync_EmptyAnswer_IsNotRemembered()
    {
        var provider = new CountingProvider(Array.Empty<ISearchResult>());

        await QuickLaunchSourceReader.LoadProviderAsync("source", provider, CancellationToken.None);
        await QuickLaunchSourceReader.LoadProviderAsync("source", provider, CancellationToken.None);

        Assert.AreEqual(2, provider.Calls);
    }

    [TestMethod]
    public async Task LoadProviderAsync_ProviderThatThrows_AnswersEmptyAndIsNotRemembered()
    {
        var provider = new CountingProvider(new IOException("the service is not up yet"));

        Assert.IsEmpty(await QuickLaunchSourceReader.LoadProviderAsync("source", provider, CancellationToken.None));
        Assert.IsEmpty(await QuickLaunchSourceReader.LoadProviderAsync("source", provider, CancellationToken.None));
        Assert.AreEqual(2, provider.Calls, "a failure is not an answer worth keeping");
    }

    [TestMethod]
    public async Task LoadProviderAsync_SupersededLoad_IsShownButNotRemembered()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var provider = new CountingProvider(Results(@"C:\one"));

        await QuickLaunchSourceReader.LoadProviderAsync("source", provider, cancellation.Token);

        Assert.IsFalse(QuickLaunchSourceCache.TryGet("source", out _),
            "a superseded load may have stopped early, so it must not stand in for the next summon");
    }

    [TestMethod]
    public async Task LoadProviderAsync_AfterInvalidate_AsksTheProviderAgain()
    {
        var provider = new CountingProvider(Results(@"C:\one"));

        await QuickLaunchSourceReader.LoadProviderAsync("source", provider, CancellationToken.None);
        QuickLaunchSourceCache.Invalidate();
        await QuickLaunchSourceReader.LoadProviderAsync("source", provider, CancellationToken.None);

        Assert.AreEqual(2, provider.Calls, "a settings save is what makes these answers stop being answers");
    }

    private static IReadOnlyList<ISearchResult> Results(string path)
        => new[] { new SimpleSearchResult { Name = path, FullPath = path } };

    private sealed class CountingProvider : IQuickPanelTabProvider
    {
        private readonly IReadOnlyList<ISearchResult>? _items;
        private readonly Exception? _failure;
        private int _calls;

        public CountingProvider(IReadOnlyList<ISearchResult> items) => _items = items;

        public CountingProvider(Exception failure) => _failure = failure;

        public int Calls => Volatile.Read(ref _calls);

        public Task<IReadOnlyList<ISearchResult>> GetEntriesAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);
            return _failure != null
                ? Task.FromException<IReadOnlyList<ISearchResult>>(_failure)
                : Task.FromResult(_items!);
        }
    }
}
