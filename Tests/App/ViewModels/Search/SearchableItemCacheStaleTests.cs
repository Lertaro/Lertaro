using Lertaro.App.ViewModels.Search;
using Lertaro.Core.SearchIndex;
using Lertaro.PluginSdk.Abstractions.Plugins;

namespace Lertaro.App.Tests.ViewModels.Search;

// Stale provider entries remain available during reload.
[TestClass]
public sealed class SearchableItemCacheStaleTests
{
    private static string NewProviderId() => "TestProvider_" + Guid.NewGuid().ToString("N");

    [TestMethod]
    public void InvalidatingKeepsServingWhatItAlreadyHad()
    {
        var id = NewProviderId();
        SearchableItemCache.Seed(id, new List<SearchableItemCache.CacheEntry>
        {
            new(new PluginSdk.Abstractions.Plugins.SearchableItem { Title = "Notepad" }, new List<string>(), null),
        });

        SearchableItemCache.Invalidate(id);

        Assert.IsTrue(SearchableItemCache.TryGetEntries(id, out var entries), "the provider dropped out of results while it reloaded");
        Assert.HasCount(1, entries);
    }

    // Marked stale, so the next EnsureLoaded rebuilds rather than deciding it is already cached.
    [TestMethod]
    public void InvalidatingStillMarksItForReload()
    {
        var id = NewProviderId();
        SearchableItemCache.Seed(id, new List<SearchableItemCache.CacheEntry>());

        SearchableItemCache.Invalidate(id);

        Assert.IsTrue(SearchableItemCache.IsStale(id));
    }

    // Nothing cached yet means nothing to serve; there is no state worth carrying and the first load
    // has not run.
    [TestMethod]
    public void InvalidatingSomethingNeverLoadedIsHarmless()
    {
        var id = NewProviderId();

        SearchableItemCache.Invalidate(id);

        Assert.IsFalse(SearchableItemCache.TryGetEntries(id, out _));
        Assert.IsFalse(SearchableItemCache.IsStale(id));
    }

    [TestMethod]
    public void ReadinessSnapshot_DistinguishesReadyWithHitsFromStale()
    {
        var provider = new ReadinessProvider();
        var id = provider.GetType().Name;
        SearchableItemCache.Seed(id,
        [
            new SearchableItemCache.CacheEntry(
                new SearchableItem { Title = "App" },
                new List<string>(),
                null)
        ]);

        var ready = SearchableItemCache.ReadinessSnapshot([provider],
            new Dictionary<string, int> { [id] = 1 }).Single();
        Assert.AreEqual(RankingProviderState.ReadyWithItems, ready.State);
        Assert.AreEqual(1, ready.ItemCount);
        Assert.AreEqual(1, ready.HitCount);

        SearchableItemCache.Invalidate(id);
        var stale = SearchableItemCache.ReadinessSnapshot([provider]).Single();
        Assert.AreEqual(RankingProviderState.Stale, stale.State);
    }

    private sealed class ReadinessProvider : ISearchableItemProvider
    {
        public event Action? ItemsChanged;
        public IEnumerable<SearchableItem> GetSearchableItems() => Array.Empty<SearchableItem>();
    }
}
