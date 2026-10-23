using System.Collections.Concurrent;
using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.App.Services;

using Lertaro.App.Services.Plugin;
using Lertaro.Core.SearchIndex;
namespace Lertaro.App.ViewModels.Search;

// Per-provider cache lifecycle. SearchableItemMapper owns matching and row construction.
internal static class SearchableItemCache
{
    public sealed record CacheEntry(SearchableItem Item, List<string> Aliases, System.Windows.Media.ImageSource? Icon)
    {
        public string[] KeywordWords { get; } = Item.Keywords.Count == 0 ? [] : Item.Keywords.Prepend(Item.Title)
            .SelectMany(text => text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        // Provider aliases are copied once on the background load. The query path reads only this bounded
        // host-owned snapshot; it never asks a provider to resolve a shortcut or inspect target metadata.
        public IReadOnlyList<SearchableItemAlias> ProviderAliases { get; } = Item.Aliases
            .Where(alias => !string.IsNullOrWhiteSpace(alias.Text))
            .Take(128)
            .Select(alias => alias with { Text = alias.Text.Trim() })
            .Where(alias => alias.Text.Length <= 128)
            .GroupBy(alias => alias.Text, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Take(32)
            .ToArray();
    }

    private static readonly ConcurrentDictionary<string, List<CacheEntry>> _cache = new();
    private static readonly ConcurrentDictionary<string, Task> _loadingTasks = new();
    private static readonly ConcurrentDictionary<string, bool> _subscribed = new();

    // Translated text and active aliases are captured at load time; language changes invalidate them.
    static SearchableItemCache() => TranslationManager.Instance.PropertyChanged += (_, _) => Clear();

    private static int _watchingComponentChanges;
    private static int _generation;

    // Subscribe lazily to avoid re-entering PluginManager initialization.
    private static void WatchComponentChanges()
    {
        if (Interlocked.Exchange(ref _watchingComponentChanges, 1) == 0)
            PluginManager.Instance.ComponentsRefreshed += Clear;
    }

    // Stale entries remain readable while their provider reloads.
    private static readonly ConcurrentDictionary<string, bool> _stale = new();

    private static void Clear()
    {
        // Language and component changes invalidate both content and aliases.
        Interlocked.Increment(ref _generation);
        _cache.Clear();
        _loadingTasks.Clear();
        _stale.Clear();
    }

    // Raised on a background thread after a provider load; subscribers rerun live search on the UI thread.
    public static event Action? ProviderLoaded;

    public static void Preload()
    {
        foreach (var provider in PluginManager.Instance.SearchableItemProviders)
            EnsureLoaded(provider);
    }

    public static bool TryGetEntries(string providerId, out List<CacheEntry> entries) => _cache.TryGetValue(providerId, out entries!);

    /// <summary>Returns provider readiness without starting or waiting for a provider.</summary>
    internal static IReadOnlyList<RankingProviderTrace> ReadinessSnapshot(
        IEnumerable<ISearchableItemProvider> providers,
        IReadOnlyDictionary<string, int>? hitCounts = null)
    {
        var snapshot = new List<RankingProviderTrace>();
        foreach (var provider in providers)
        {
            var id = provider.GetType().Name;
            var hasEntries = _cache.TryGetValue(id, out var entries);
            var state = _stale.ContainsKey(id)
                ? RankingProviderState.Stale
                : hasEntries
                    ? entries!.Count == 0 ? RankingProviderState.ReadyEmpty : RankingProviderState.ReadyWithItems
                    : _loadingTasks.ContainsKey(id)
                        ? RankingProviderState.Loading
                        : RankingProviderState.NotStarted;
            var hitCount = hitCounts != null && hitCounts.TryGetValue(id, out var hits) ? hits : 0;
            snapshot.Add(new RankingProviderTrace(
                id,
                state,
                hasEntries ? entries!.Count : 0,
                hitCount));
        }
        return snapshot;
    }

    // Test seams for cache bookkeeping.
    internal static void Seed(string providerId, List<CacheEntry> entries) => _cache[providerId] = entries;

    internal static bool IsStale(string providerId) => _stale.ContainsKey(providerId);

    /// <summary>Marks entries stale while continuing to serve them until reload completes.</summary>
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

        // Fresh cached entries need no reload; stale entries fall through.
        if (_cache.ContainsKey(id) && !_stale.ContainsKey(id)) return;

        var generation = Volatile.Read(ref _generation);
        _loadingTasks.GetOrAdd(id, key => Task.Run(() =>
        {
            try
            {
                var rawItems = PluginPerformanceMonitor.Measure(provider, () => provider.GetSearchableItems()?.ToList() ?? new List<SearchableItem>());
                var entries = new List<CacheEntry>();
                foreach (var item in rawItems)
                {
                    if (item == null) continue;
                    var aliases = provider.EnableAlias
                        ? AliasProviderRegistry.GetActiveProviders()
                            .Where(p => PluginPerformanceMonitor.Measure(p, () => p.CanHandle(item.Title)))
                            .SelectMany(p => PluginPerformanceMonitor.Measure(p, () => p.GetAliases(item.Title)?.ToList() ?? new List<string>()))
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

    // Freeze each provider HBITMAP once and release the caller-owned GDI handle.
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
}
