using Lertaro.App.ViewModels.Search;

namespace Lertaro.App.Tests.ViewModels.Search;

// What a search gets to show while a provider is being reloaded. The rebuild itself is unavoidable --
// a change under a watched folder has to be picked up -- but it must never be something the user can
// see, and taking the entries away for its duration is exactly that: every application drops out of
// the results until each shortcut has been resolved through COM and each icon re-extracted.
//
// It matters more than it looks, because the rebuild is not always warranted: a change the index cannot
// pin to a directory is reported against the whole drive, so a busy C: still produces the occasional
// refresh for something that never touched these folders.
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
}
