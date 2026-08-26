using Lertaro.App.Services.Plugin;
using Lertaro.Core;
using Lertaro.Core.Services.Plugin.DirectoryIndex;
using Lertaro.Core.Services.Search;

namespace Lertaro.App.Services.QuickPanel;

/// <summary>
/// Turns one configured quick panel source into the entries it should show. Every kind is answered
/// from the index rather than by walking the disk -- recent files through the service's own recency
/// query, the rest through <see cref="IndexedDirectoryEnumerator"/>, which falls back to a real walk
/// only where no index covers the folder.
/// </summary>
public static class QuickPanelSourceLoader
{
    public static async Task<List<SearchResult>> LoadAsync(QuickPanelFolderSource source, CancellationToken token = default)
        => await LoadCoreAsync(source, progress: null, token).ConfigureAwait(false);

    /// <summary>
    /// Reads a source in bounded batches while still returning its complete, correctly ordered result.
    /// The progress batches are intentionally arrival order only; callers use them for an early panel
    /// paint, then replace them with the final user-selected sort once enumeration completes.
    /// </summary>
    public static async Task<List<SearchResult>> LoadProgressivelyAsync(
        QuickPanelFolderSource source,
        IProgress<IReadOnlyList<SearchResult>> progress,
        CancellationToken token = default)
        => await LoadCoreAsync(source, progress, token).ConfigureAwait(false);

    private static async Task<List<SearchResult>> LoadCoreAsync(
        QuickPanelFolderSource source,
        IProgress<IReadOnlyList<SearchResult>>? progress,
        CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(source.Path))
            return new List<SearchResult>();

        var filter = QuickPanelFilterParser.Parse(source.FilterPattern, GetGlobalTokenPrefix());

        if (source.Kind == QuickPanelSourceKind.RecentFiles)
        {
            // The recency query is the index's own: it already returns newest-first across the whole
            // subtree, so recursion and ordering are not this method's to apply.
            var recent = await new SearchService()
                .GetRecentFilesAsync(new[] { source.Path }, source.MaxItems, EffectiveMaxAge(source), token)
                .ConfigureAwait(false);
            if (filter.HasTokenFilters)
            {
                // ponytail: token filters run after the recency cap, so a filtered recent-files source
                // can show fewer than MaxItems; fetching unbounded recency first would cost too much.
                recent = await ApplyFilterAsync(recent, filter).ConfigureAwait(false);
            }
            progress?.Report(recent);
            return recent;
        }

        if (filter.HasTokenFilters)
        {
            // Enumerate without a file pattern: glob and token entries are OR-ed, so narrowing the
            // enumeration to the glob subset would silently drop every token-only match.
            var results = new List<SearchResult>();
            var batch = new List<SearchResult>(64);
            await IndexedDirectoryEnumerator.EnumerateAsync(source.Path, source.Recursive, string.Empty,
                result => AddResult(result, source.Kind, results, batch, progress), limit: 0, token).ConfigureAwait(false);

            if (batch.Count > 0)
                progress?.Report(batch);

            results = await ApplyFilterAsync(results, filter).ConfigureAwait(false);
            return Order(results, source.Kind, source.SortByModified, source.MaxItems);
        }

        var all = new List<SearchResult>();
        var normalBatch = new List<SearchResult>(64);
        await IndexedDirectoryEnumerator.EnumerateAsync(source.Path, source.Recursive, source.FilterPattern,
            result => AddResult(result, source.Kind, all, normalBatch, progress), limit: 0, token).ConfigureAwait(false);

        if (normalBatch.Count > 0)
            progress?.Report(normalBatch);

        return Order(all, source.Kind, source.SortByModified, source.MaxItems);
    }

    private static async Task<List<SearchResult>> ApplyFilterAsync(List<SearchResult> results, QuickPanelFilterSpec filter)
    {
        if (!filter.HasTokenFilters)
            return results;

        var matched = new HashSet<SearchResult>();
        if (filter.GlobPatterns.Length > 0)
        {
            foreach (var result in results)
            {
                if (FilterPatternHelper.Matches(result.Name, filter.GlobPatterns))
                    matched.Add(result);
            }
        }

        foreach (var token in filter.TokenFilters)
        {
            var provider = PluginManager.Instance.QueryTokenProviders.FirstOrDefault(p => p.CanHandle(token));
            if (provider == null)
                continue; // entry did not fully match search syntax -- ignore it

            var filtered = await provider.ApplyAsync(token, results);
            foreach (var item in filtered)
            {
                if (item is SearchResult sr)
                    matched.Add(sr);
            }
        }

        return results.Where(matched.Contains).ToList();
    }

    private static char GetGlobalTokenPrefix()
    {
        var prefix = UserSettings.Load().GlobalTokenPrefix;
        return string.IsNullOrEmpty(prefix) ? ':' : prefix[0];
    }

    private static void AddResult(
        SearchResult result,
        QuickPanelSourceKind kind,
        List<SearchResult> results,
        List<SearchResult> batch,
        IProgress<IReadOnlyList<SearchResult>>? progress)
    {
        if ((kind == QuickPanelSourceKind.FoldersOnly && !result.IsDir)
            || (kind == QuickPanelSourceKind.FilesOnly && result.IsDir))
            return;

        results.Add(result);
        batch.Add(result);
        if (batch.Count < 64)
            return;

        progress?.Report(batch.ToList());
        batch.Clear();
    }

    /// <summary>
    /// The order a kind implies, and its cap. Recent-files sources never reach here: their order comes
    /// from the index query itself.
    /// </summary>
    internal static List<SearchResult> Order(List<SearchResult> results, QuickPanelSourceKind kind, int maxItems)
        => Order(results, kind, sortByModified: kind == QuickPanelSourceKind.AllByModified, maxItems);

    internal static List<SearchResult> Order(List<SearchResult> results, QuickPanelSourceKind kind, bool sortByModified, int maxItems)
    {
        var useModifiedSort = sortByModified || kind == QuickPanelSourceKind.AllByModified;
        IEnumerable<SearchResult> ordered = useModifiedSort
            ? results.OrderByDescending(r => r.Metadata.Modified)
            : results.OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase);
        return (maxItems > 0 ? ordered.Take(maxItems) : ordered).ToList();
    }

    // 0 means "no age limit" in the settings, but the recency query reads 0 as "nothing qualifies", so
    // it has to be spelled as a ceiling instead. 30 days is the same bound the Startup Panel's own
    // field allows.
    private static int EffectiveMaxAge(QuickPanelFolderSource source)
        => source.MaxAgeMinutes > 0 ? source.MaxAgeMinutes : 43200;
}
