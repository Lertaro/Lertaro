using System.Collections.Concurrent;
using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.App.Services;

using Lertaro.App.Services.Plugin;
using Lertaro.Core.SearchIndex;
namespace Lertaro.App.ViewModels.Search;

// Owns the per-provider load/cache lifecycle for ISearchableItemProvider -- split out of
// SearchableItemMapper purely to keep that file under the file-length limit; SearchableItemMapper
// still owns the actual query-matching and AppSearchResult-building logic.
internal static class SearchableItemCache
{
    public sealed record CacheEntry(SearchableItem Item, List<string> Aliases, System.Windows.Media.ImageSource? Icon);

    private static readonly ConcurrentDictionary<string, List<CacheEntry>> _cache = new();
    private static readonly ConcurrentDictionary<string, Task> _loadingTasks = new();
    private static readonly ConcurrentDictionary<string, bool> _subscribed = new();

    // A cached CacheEntry bakes each provider's translated Title/Description into a plain string at
    // load time (see EnsureLoaded) -- unlike XAML's indexer bindings, that snapshot has no way to
    // notice a later language switch on its own. Providers only invalidate their own cache entry via
    // ItemsChanged, which most never fire for a language change (it's meant for the provider's own
    // underlying data changing, e.g. Start Menu file-system events) -- so without this, every cached
    // provider's item text stays frozen in whatever language was active the first time it loaded.
    //
    // The same applies to the alias list each entry carries, which EnsureLoaded builds from the
    // ENABLED alias providers at that moment. Turning one off afterwards left those aliases baked in,
    // so a settings item kept being found by a disabled provider's spelling while the highlight -- which
    // is recomputed live and does honour the setting -- came back empty: found but not lit up.
    static SearchableItemCache() => TranslationManager.Instance.PropertyChanged += (_, _) => Clear();

    private static int _watchingComponentChanges;
    private static int _generation;

    // Subscribed on first real use rather than from the static constructor. Reading
    // PluginManager.Instance during type initialization would touch a Lazy singleton whose own
    // constructor loads every plugin -- and if that path is ever made to touch this cache, type
    // initialization would re-enter the Lazy that is still initializing. It does not today, but the
    // constructor already carries a comment about having been caught by exactly that once. By the time
    // anything asks this cache for entries, the singleton is long since built.
    private static void WatchComponentChanges()
    {
        if (Interlocked.Exchange(ref _watchingComponentChanges, 1) == 0)
            PluginManager.Instance.ComponentsRefreshed += Clear;
    }

    // Providers whose entries are known to be out of date but are still worth showing until the fresh
    // ones arrive -- see Invalidate.
    private static readonly ConcurrentDictionary<string, bool> _stale = new();

    private static void Clear()
    {
        // Dropped outright, unlike Invalidate below: a language switch or a component being turned off
        // makes the cached entries WRONG, not merely old, and showing the previous language for another
        // second is not a kindness.
        Interlocked.Increment(ref _generation);
        _cache.Clear();
        _loadingTasks.Clear();
        _stale.Clear();
    }

    // Providers load on a background thread and a query issued before a given provider finishes is
    // silently missing its items -- there is no synchronous "wait for everything" alternative without
    // blocking the UI. Instead, a live search re-runs itself once more providers become available, so
    // results stream in rather than staying incomplete for the rest of the session. Raised on a
    // background thread; subscribers must marshal back to the UI thread themselves.
    public static event Action? ProviderLoaded;

    public static void Preload()
    {
        foreach (var provider in PluginManager.Instance.SearchableItemProviders)
            EnsureLoaded(provider);
    }

    public static bool TryGetEntries(string providerId, out List<CacheEntry> entries) => _cache.TryGetValue(providerId, out entries!);

    // Test seams. Loading for real needs a live PluginManager and a provider that reads the machine's
    // own Start Menu, neither of which a test has -- but what has to hold is about the cache's own
    // bookkeeping around a reload, which these make reachable without either.
    internal static void Seed(string providerId, List<CacheEntry> entries) => _cache[providerId] = entries;

    internal static bool IsStale(string providerId) => _stale.ContainsKey(providerId);

    /// <summary>Marks a provider's entries as out of date, without taking them away.</summary>
    /// <remarks>
    /// The entries stay and keep being served while the reload runs. Removing them meant every search in
    /// the meantime silently lost that provider -- for the Start Menu, every application vanished from
    /// the results for as long as a full re-scan took (each shortcut resolved through COM, each icon
    /// re-extracted), then reappeared. That is a rebuild the user should never have been able to see.
    ///
    /// It matters more than it looks, because a rebuild is not always warranted: a change the index
    /// cannot pin to a directory is reported against the whole drive (see
    /// PluginDirectoryChangeNotifier), so a busy C: still produces the occasional refresh for something
    /// that never touched this provider's folders. Serving the old list makes that miss cost nothing
    /// visible rather than emptying the results on a false alarm.
    /// </remarks>
    public static void Invalidate(string providerId)
    {
        if (!_cache.ContainsKey(providerId))
            return;

        _stale[providerId] = true;
        // The finished load is what would otherwise stop EnsureLoaded from starting a fresh one.
        _loadingTasks.TryRemove(providerId, out _);
    }

    public static void EnsureLoaded(ISearchableItemProvider provider)
    {
        WatchComponentChanges();
        var id = provider.GetType().Name;
        if (_subscribed.TryAdd(id, true))
        {
            provider.ItemsChanged += () => Invalidate(id);
        }

        // Stale entries are served, but they still have to be replaced -- so a cached-and-stale provider
        // falls through to the load below rather than returning here.
        if (_cache.ContainsKey(id) && !_stale.ContainsKey(id)) return;

        // Named, not a discard: `_` inside this lambda would shadow the discard the body wants to use.
        var generation = Volatile.Read(ref _generation);
        _loadingTasks.GetOrAdd(id, key => Task.Run(() =>
        {
            try
            {
                var rawItems = provider.GetSearchableItems() ?? Array.Empty<SearchableItem>();
                var entries = new List<CacheEntry>();
                foreach (var item in rawItems)
                {
                    if (item == null) continue;
                    var aliases = provider.EnableAlias
                        ? AliasProviderRegistry.GetActiveProviders()
                            .Where(p => p.CanHandle(item.Title))
                            .SelectMany(p => p.GetAliases(item.Title))
                            .ToList()
                        : new List<string>();
                    entries.Add(new CacheEntry(item, aliases, MaterializeIcon(item)));
                }
                if (generation != Volatile.Read(ref _generation))
                    return;
                _cache[id] = entries;
                _stale.TryRemove(id, out _);
            }
            catch (Exception ex)
            {
                Core.Logger.Log($"[SearchableItemCache] Error loading from provider '{provider.Name}': {ex.Message}", Core.LogLevel.Error);
                // Only when there is nothing to fall back on. A reload that failed is no reason to throw
                // away the entries that were working a moment ago -- the empty list would be served for
                // the rest of the session, since nothing retries until the next change.
                if (generation == Volatile.Read(ref _generation))
                {
                    _cache.TryAdd(id, new List<CacheEntry>());
                    _stale.TryRemove(id, out _);
                }
            }
            finally
            {
                ProviderLoaded?.Invoke();
            }
        }));
    }

    // Convert a provider's raw GDI HBITMAP into a frozen, thread-safe BitmapSource ONCE at load time,
    // then release the GDI handle immediately. Providers hand us HBitmapIcon under a "caller must
    // DeleteObject" contract; materializing + freeing here avoids leaking one GDI handle per cached
    // item (which scales with the number of installed apps) and avoids rebuilding the bitmap on every
    // keystroke.
    private static System.Windows.Media.ImageSource? MaterializeIcon(SearchableItem item)
    {
        var hBitmap = item.HBitmapIcon;
        if (hBitmap == IntPtr.Zero) return null;
        try
        {
            var src = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                hBitmap, IntPtr.Zero,
                System.Windows.Int32Rect.Empty,
                System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
            src.Freeze();
            return src;
        }
        catch
        {
            return null;
        }
        finally
        {
            DeleteObject(hBitmap);
            item.HBitmapIcon = IntPtr.Zero;
        }
    }

    [System.Runtime.InteropServices.DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    // True when `targetFileFilterKind` (e.g. "FileFilter_tf") corresponds to an actually-registered
    // file filter, i.e. some loaded provider has an item with that ResultKind. Used to decide whether
    // a keyword search is a real filter prefix that should hide general items.
    public static bool IsRegisteredFilterKeyword(string targetFileFilterKind)
    {
        foreach (var provider in PluginManager.Instance.SearchableItemProviders)
        {
            if (TryGetEntries(provider.GetType().Name, out var entries) &&
                entries.Any(e => string.Equals(e.Item.ResultKind, targetFileFilterKind, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }
        return false;
    }
}
