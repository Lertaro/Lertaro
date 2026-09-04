using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.Core;

using Lertaro.App.Services.Plugin;
using Lertaro.App.Services.ShellIcons;
using Lertaro.Core.SearchIndex;
namespace Lertaro.App.ViewModels.Search.Mapping;

public static class SearchableItemMapper
{
    public static void Preload() => SearchableItemCache.Preload();

    // A learned Application history entry retains only its launched path. Reuse the cached provider
    // entry when it is still available so history results keep the application's own title, icon, and
    // execution delegate instead of reconstructing an almost-identical row from the shortcut path.
    internal static bool TryCreateApplicationResult(string path, string query, out AppSearchResult result)
    {
        foreach (var provider in PluginManager.Instance.SearchableItemProviders)
        {
            SearchableItemCache.EnsureLoaded(provider);
            if (!SearchableItemCache.TryGetEntries(provider.GetType().Name, out var entries))
                continue;

            var entry = entries.FirstOrDefault(entry =>
                string.Equals(entry.Item.ResultKind, "Application", StringComparison.Ordinal) &&
                string.Equals(entry.Item.ActionArgument?.Trim(), path.Trim(), StringComparison.OrdinalIgnoreCase));
            if (entry == null)
                continue;

            result = BuildCandidate(entry, provider, query, 0).Result;
            return true;
        }

        result = null!;
        return false;
    }

    // Providers load on a background thread and a query issued before a given provider finishes is
    // silently missing its items (see AddSearchableItemResults' cache-miss "continue" below) -- there is
    // no synchronous "wait for everything" alternative without blocking the UI. Instead, a live search
    // re-runs itself once more providers become available, so results stream in rather than staying
    // incomplete for the rest of the session. Raised on a background thread; subscribers must marshal
    // back to the UI thread themselves.
    public static event Action? ProviderLoaded
    {
        add => SearchableItemCache.ProviderLoaded += value;
        remove => SearchableItemCache.ProviderLoaded -= value;
    }

    // Returns candidates (with their ranking weight) instead of appending directly to a results list --
    // the caller (SearchResultMapper.BuildQuickResults) merges these into one globally weight-sorted
    // list alongside favorites/history-matched files/file-search results, rather than always showing
    // every searchable item ahead of every file result regardless of which actually matched better.
    public static List<(AppSearchResult Result, double Weight)> CollectSearchableItemResults(string query, bool isInlineWindow)
    {
        var candidates = new List<(AppSearchResult Result, double Weight)>();
        if (isInlineWindow) return candidates;

        var q = query?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(q)) return candidates;

        // Every matched entry -- across ALL providers, not just within one -- gets ranked by the same
        // percentage*consecutiveness weight the file search hot path uses (FuzzyMatcher.
        // ComputeMatchWeight, against the entry's own title -- same text TextHighlighter shows),
        // instead of a fixed match-kind bucket order capped PER PROVIDER.
        var matched = new List<(SearchableItemCache.CacheEntry Entry, double Weight, ISearchableItemProvider Provider, string ActiveQuery)>();

        foreach (var provider in PluginManager.Instance.SearchableItemProviders)
        {
            SearchableItemCache.EnsureLoaded(provider);

            if (!SearchableItemCache.TryGetEntries(provider.GetType().Name, out var entries))
                continue;

            foreach (var entry in entries)
            {
                // The standard match+weight contract (FuzzyMatcher.ComputeBestMatch): title first,
                // then each curated alias, via the same FzfPattern.Parse Core's real file search uses
                // -- a multi-word query like "gsh ypfq" correctly requires BOTH words to match
                // somewhere. (Keyword-scoped directory search used to live here as a FileFilter_
                // ResultKind routing over materialized files; it is now a real scoped engine search --
                // see FileFilterScopeResolver.)
                var (isMatch, weight) = FuzzyMatcher.ComputeBestMatch(q, entry.Item.Title, entry.Aliases);
                if (isMatch)
                    matched.Add((entry, weight, provider, q));
            }
        }

        // Generous safety cap only -- the real top-N selection happens after this merges with the
        // other candidate categories in BuildQuickResults.
        var matches = matched.OrderByDescending(m => m.Weight).Take(50);
        foreach (var (entry, weight, provider, activeQuery) in matches)
        {
            candidates.Add(BuildCandidate(entry, provider, activeQuery, weight));
        }

        return candidates;
    }

    // Split out of the matches loop below purely to keep this file's per-method length down -- no
    // other caller.
    private static (AppSearchResult Result, double Weight) BuildCandidate(SearchableItemCache.CacheEntry entry, ISearchableItemProvider provider, string activeQuery, double weight)
    {
        var item = entry.Item;
        System.Windows.Media.ImageSource? iconOverride = null;

        var isRealFile = false;
        var isRealDir = false;
        var isApplication = false;
        var rKind = item.ResultKind ?? string.Empty;

        if (rKind == "File")
        {
            isRealFile = true;
        }
        else if (rKind == "Directory")
        {
            isRealDir = true;
        }
        else if (rKind == "Application")
        {
            // Keep the app's real target path (a Start Menu .lnk, or a virtual shell:AppsFolder
            // token for packaged apps) instead of the generic "__SEARCHABLE_ITEM__:" placeholder,
            // so file actions (copy, locate in explorer, ...) have something to act on -- each
            // action's own CanExecute already handles a path that doesn't exist on disk.
            isApplication = true;
        }

        if (entry.Icon != null)
        {
            // Frozen bitmap materialized once at load time (see EnsureLoaded); reused as-is with
            // no per-keystroke rebuild and no leaked GDI handle.
            iconOverride = entry.Icon;
        }
        else if ((isRealFile || isRealDir || isApplication) && !string.IsNullOrWhiteSpace(item.ActionArgument))
        {
            // Deliberately leave IconOverride unset here -- AppSearchResult.Icon's own getter (FullPath/
            // IsDir are set below to item.ActionArgument/isRealDir, exactly what this needs) already
            // does a cache-only check first, then a background-thread ShellIconHelper extraction
            // (semaphore-limited, marshaled back once ready) the same way every regular file search
            // result already gets its icon. Calling ShellIconHelper.GetIconForPath eagerly here instead
            // would set IconOverride and short-circuit that whole lazy/async path, forcing every File
            // Filter/Application/Settings item through a SYNCHRONOUS extraction on the UI thread -- for
            // a video file on a network drive, that means Windows' shell thumbnail handler reading the
            // file over SMB to decode a frame, blocking the search results render until it returns.
        }
        else if (!string.IsNullOrWhiteSpace(item.IconData))
        {
            try
            {
                var color = string.IsNullOrWhiteSpace(item.IconColor) ? "DefaultPluginIconColor" : item.IconColor;
                iconOverride = ShellIconHelper.CreateVectorIcon(item.IconData, color);
            }
            catch (Exception ex)
            {
                Logger.Log($"[SearchableItemMapper] Failed to create vector icon: {ex.Message}", LogLevel.Error);
            }
        }
        else
        {
            try
            {
                iconOverride = ShellIconHelper.CreateVectorIcon("M7 2v11h3v9l7-12h-4l3-8z", "DefaultPluginIconColor");
            }
            catch { }
        }

        return (new AppSearchResult
        {
            Name = item.Title,
            FullPath = (isRealFile || isRealDir || isApplication) ? item.ActionArgument : $"__SEARCHABLE_ITEM__:{provider.Name}:{item.Title}",
            // Applications show name-only: blank the subtitle so the path row collapses (an app's
            // FullPath is a virtual token anyway). Other item kinds keep their description.
            ParentDir = item.ResultKind == "Application" ? string.Empty : item.Description,
            IsDir = isRealDir,
            Drive = string.Empty,
            ResultKind = isRealFile ? "File" : (isRealDir ? "Directory" : (isApplication ? "Application" : "InstantResult")),
            SearchQuery = activeQuery ?? string.Empty,
            IconOverride = iconOverride,
            InstantResultActionType = item.ActionType ?? "Copy",
            InstantResultActionArgument = item.ActionArgument ?? string.Empty,
            InstantResultOnExecute = item.OnExecute,
            InstantResultOnExecuteFunc = item.OnExecuteFunc,
            TabCompletion = item.TabCompletion,
            SourceProvider = provider
        }, weight);
    }
}
