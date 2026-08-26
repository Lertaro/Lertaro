using System.IO;
using Lertaro.Core;
using Lertaro.Core.SearchIndex;

namespace Lertaro.App.ViewModels.Search.Mapping;

public static class SearchResultMapper
{
    // skipDisplayCap: token mode (SearchDispatchController.ComposeAndApplyAsync) still applies its own
    // final 50-item cap AFTER filtering by the token, but needs the FULL ranked candidate set to filter
    // over first -- capping to the usual ~50 here, before a "::xxx"/directory-segment token ever runs,
    // silently drops the token's real matches whenever they don't also happen to be in the top ~50 by
    // plain filename weight (e.g. a common substring like "1080" already fills that cap with unrelated
    // files before the directory filter gets a chance to run at all).
    public static List<AppSearchResult> BuildQuickResults(List<SearchResult>? fileResults, string query, string? scope, string? contextDirectory, bool isInlineWindow, string? rawQuery = null, bool skipDisplayCap = false)
    {
        var uiResults = new List<AppSearchResult>();
        // Instant-result plugins get the untouched raw text (keyword + any " :xxx" token suffix) rather
        // than the stripped keyword everything else here uses -- a plugin like a calculator or unit
        // converter may care about the suffix itself, and it has no other way to see it since the token
        // is consumed before reaching here for every other purpose (file search, highlighting, ...).
        PluginSearchResultMapper.AddInstantResults(uiResults, rawQuery ?? query, query, isInlineWindow);

        RemoveQueriedDirectoryItself(fileResults, query);

        // If a directory scope is provided, keep only file/folder results that reside inside the scoped path.
        if (!string.IsNullOrEmpty(scope) && fileResults != null)
        {
            var normalizedScope = SearchResultHelper.NormalizePath(scope);
            fileResults = fileResults.FindAll(x =>
            {
                var normalizedPath = SearchResultHelper.NormalizePath(x.Path);
                return SearchResultHelper.IsPathInsideScope(normalizedPath, normalizedScope)
                    && !string.Equals(normalizedPath, normalizedScope, StringComparison.OrdinalIgnoreCase);
            });
        }

        // Plugin actions keep their own grouped-by-GroupName display (unlike everything below, these
        // are explicit keyword triggers the user deliberately typed, not fuzzy-guessed candidates, so
        // "how well did this match the query text" isn't a meaningful way to rank them against files/
        // apps/favorites) -- positioned right after instant results, before the weighted candidates.
        var hasPluginSearchActions = PluginSearchResultMapper.AddPluginSearchActionResults(uiResults, query, contextDirectory, isInlineWindow);

        var historySnapshot = SearchHistoryStore.Snapshot();

        // Quick-window-only: a user-orderable hard tier between history priority and match-quality
        // weight (RankedCandidate.TypeRank) -- lets e.g. Applications always outrank Files regardless
        // of which matched the query text better, without a small weight bonus getting lost against a
        // much better textual match. Empty by default (every candidate's Rank falls back to the same
        // int.MaxValue), so an untouched order list is a complete no-op. See SearchResultTypePriority.
        var typeOrder = isInlineWindow ? new List<string>() : UserSettings.Load().ResultTypeOrder;

        // Quick-window-only exclusive filter: if the first character the user actually typed matches a
        // configured per-type trigger (UserSettings.ResultTypeTriggers), only that type's candidates
        // enter the ranked competition below -- Favorites/history are unaffected, they're hardcoded
        // top-priority regardless. rawQuery (not the sort/exclusion-token-stripped query) is what's
        // probed since it's the one guaranteed to still have whatever the user actually typed first,
        // including a literal space -- see PluginSearchResultMapper.AddInstantResults' own use of it
        // above for the same reason. query itself already arrives with the trigger character stripped
        // (SearchDispatchController.StripResultTypeTrigger removes it before the file-index engine ever
        // sees it), so it's used as-is below for matching/highlighting -- the trigger character never
        // shows up highlighted and never pollutes the fuzzy match, with no second stripping needed here.
        string? triggeredTypeId = null;
        if (!isInlineWindow)
        {
            var probe = rawQuery ?? query;
            if (probe.Length > 0)
                triggeredTypeId = SearchResultTypePriority.ResolveTrigger(probe[0], UserSettings.Load().ResultTypeTriggers);
        }

        // Favorites, history-matched files, searchable items (apps/settings), and remaining file
        // results all compete on ONE list now: history priority first (an explicit "you've opened
        // this before" signal -- items with no history sort after every item that has one), then
        // match-quality weight -- instead of every favorite always beating every app always beating
        // every file regardless of which one actually matched the query text better.
        var candidates = new List<RankedCandidate>();

        // The history entry already remembers which query opened its path. Re-introduce existing paths
        // whose learned keyword matches this query even when the index's bounded first page omitted them.
        // Inline results enter Global Search here; its separately scoped Current Folder tier wins the
        // downstream path dedupe. An explicit type trigger retains its strict result domain.
        if (triggeredTypeId == null)
            candidates.AddRange(HistorySearchCandidateMapper.Collect(query, scope));

        if (!string.IsNullOrWhiteSpace(query))
        {
            var favorites = UserSettings.Load().Favorites;
            for (var i = 0; i < favorites.Count; i++)
            {
                var fav = favorites[i];
                if (!Helpers.FavoritePathResolver.IsPathAvailable(fav.Path)) continue;
                var (isMatch, weight) = FavoriteSearchHelper.ComputeMatch(fav, query);
                if (!isMatch)
                    continue;

                // A favorite is curated by the user regardless of whether it also has USAGE history --
                // that's a stronger signal than "matched the query text well" or "happens to be an
                // application", so it stays ahead of both. TypeRank never actually differentiates a
                // favorite from anything else (IsCurated already does), so it's given the Files id here
                // simply as the closest match to what a favorited path actually is.
                var normalizedFavPath = Helpers.FavoritePathResolver.NormalizeForComparison(fav.Path);
                var priority = historySnapshot.TryGetValue(normalizedFavPath, out var hp) ? hp : int.MaxValue;
                candidates.Add(new RankedCandidate(
                    FavoriteSearchHelper.CreateFavoriteUiResult(fav, query, 0),
                    IsCurated: true,
                    priority,
                    SearchResultTypePriority.Rank(SearchResultTypePriority.FilesTypeId, typeOrder),
                    weight,
                    normalizedFavPath));
            }
        }

        foreach (var (result, weight) in SearchableItemMapper.CollectSearchableItemResults(query, isInlineWindow))
        {
            var typeId = result.SourceProvider is PluginSdk.Abstractions.Plugins.ISearchableItemProvider provider
                ? SearchResultTypePriority.GetProviderTypeId(provider)
                : SearchResultTypePriority.FilesTypeId;
            if (triggeredTypeId != null && typeId != triggeredTypeId)
                continue;

            // An application's FullPath can be a virtual shell:AppsFolder\{AUMID} id (packaged apps) --
            // Path.GetFullPath (inside NormalizePath) would mangle that, and SearchHistoryStore itself
            // never runs it through NormalizePath either (see SearchHistoryStore.RecordCore), so the
            // lookup key has to skip it here too or an app's history priority would never resolve.
            var lookupPath = result.IsApplication ? result.FullPath.Trim() : SearchResultHelper.NormalizePath(result.FullPath);
            var hasHistory = historySnapshot.TryGetValue(lookupPath, out var priority);
            candidates.Add(new RankedCandidate(
                result,
                IsCurated: hasHistory,
                hasHistory ? priority : int.MaxValue,
                SearchResultTypePriority.Rank(typeId, typeOrder),
                weight,
                SearchResultHelper.NormalizePath(result.FullPath)));
        }

        // Only entered when nothing is triggered, or "Files" itself is the triggered type -- fileResults
        // was already fetched from the backend using the trigger-stripped query (SearchDispatchController
        // strips it before ever dispatching the search), so this gets the same clean-text recall any
        // other type gets, not just whatever a trigger-polluted query happened to match.
        if (fileResults != null && (triggeredTypeId == null || triggeredTypeId == SearchResultTypePriority.FilesTypeId))
        {
            foreach (var result in fileResults)
            {
                var lookupPath = result.Path.Length > 3 && result.Path[^1] == '\\' ? result.Path.TrimEnd('\\') : result.Path;
                var hasHistory = historySnapshot.TryGetValue(lookupPath, out var priority);
                candidates.Add(new RankedCandidate(
                    SearchResultHelper.CreateUiResult(result, query, 0, isApplication: false, scope),
                    IsCurated: hasHistory,
                    hasHistory ? priority : int.MaxValue,
                    SearchResultTypePriority.Rank(SearchResultTypePriority.FilesTypeId, typeOrder),
                    FuzzyMatcher.ComputeMatchWeight(result.Name, query),
                    SearchResultHelper.NormalizePath(result.Path)));
            }
        }

        var ranked = RankAndDedupe(candidates);

        // Capped here (not deferred to the caller) because this display cap has to respect whatever
        // header/grouping layout the caller (or InlineListSearchHelper.MergeLocalMatches, downstream)
        // builds around these rows -- e.g. the inline window's "Current Folder"/"Global Search" split
        // needs its own files to stay adjacent to its own header. SearchDispatchController only takes
        // over capping/filtering once a query token is active, since token mode collapses that
        // grouping anyway (see its own composition logic). Same two-tier shape as before (show
        // everything under 10 total, else pad to ~8 then allow up to 50 with a "N more" marker) --
        // just applied to the now-unified candidate list instead of only file results.
        if (skipDisplayCap || uiResults.Count + ranked.Count < 10)
        {
            foreach (var result in ranked)
            {
                result.Index = uiResults.Count;
                uiResults.Add(result);
            }

            return uiResults;
        }

        var firstCount = Math.Min(ranked.Count, Math.Max(0, 8 - uiResults.Count));
        for (var i = 0; i < firstCount; i++)
        {
            ranked[i].Index = uiResults.Count;
            uiResults.Add(ranked[i]);
        }

        var hasMoreAtEnd = ranked.Count > 50;
        var endLimit = hasMoreAtEnd ? 50 : ranked.Count;

        for (var i = firstCount; i < endLimit; i++)
        {
            ranked[i].Index = uiResults.Count;
            uiResults.Add(ranked[i]);
        }

        if (hasMoreAtEnd)
        {
            SearchResultHelper.AddShowMoreResult(uiResults, query);
        }

        return uiResults;
    }

    // TypeRank: this candidate's position in UserSettings.ResultTypeOrder (see SearchResultTypePriority),
    // int.MaxValue for the inline window and for any type the user hasn't ordered -- a plain, uniform
    // tiebreaker that leaves Weight fully in control until the user actually orders something.
    internal readonly record struct RankedCandidate(AppSearchResult Result, bool IsCurated, int Priority, int TypeRank, double Weight, string NormalizedPath);

    // Shared by both search groups the inline window shows (its own "Current Folder" matches via
    // ExplorerSearchHelper, and this "Global Search" tier below) so a file scores the same way
    // regardless of which of the two it happens to land in: favorites/history-matched entries (an
    // explicit "you use/opened this" signal) outrank everything else, then the user's own type-priority
    // order (e.g. Applications over Files, quick window only), then match-quality weight, then shorter
    // path, then alphabetically.
    internal static List<AppSearchResult> RankAndDedupe(List<RankedCandidate> candidates)
    {
        var usedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ranked = new List<AppSearchResult>();
        foreach (var candidate in candidates
                     .OrderByDescending(c => c.IsCurated)
                     .ThenBy(c => c.Priority)
                     .ThenBy(c => c.TypeRank)
                     .ThenByDescending(c => c.Weight)
                     .ThenBy(c => c.NormalizedPath.Length)
                     .ThenBy(c => c.NormalizedPath, StringComparer.OrdinalIgnoreCase))
        {
            if (usedPaths.Add(candidate.NormalizedPath))
                ranked.Add(candidate.Result);
        }
        return ranked;
    }

    public static AppSearchResult CreateUiResult(SearchResult item, string query, int index, bool isApplication, string? scope)
        => SearchResultHelper.CreateUiResult(item, query, index, isApplication, scope);

    public static AppSearchResult CreateNoResultsResult(string query)
        => SearchResultHelper.CreateNoResultsResult(query);

    public static AppSearchResult CreateResultTypeTriggerPromptResult(string typeDisplayName)
        => SearchResultHelper.CreateResultTypeTriggerPromptResult(typeDisplayName);

    public static AppSearchResult CreateKeepTypingPromptResult()
        => SearchResultHelper.CreateKeepTypingPromptResult();

    public static string FormatSearchStatus(int appCount, int fileCount)
        => SearchResultHelper.FormatSearchStatus(appCount, fileCount);

    public static void AddSectionHeader(List<AppSearchResult> uiResults, string title, string query)
        => SearchResultHelper.AddSectionHeader(uiResults, title, query);

    // A query that's an exact directory path ("c:\", "c:\Users\") is a request to browse INTO that
    // directory, not a request to find it -- the index still returns the directory itself as a matching
    // record (hence the synthetic all-zero modified date some callers render for it), so every caller
    // that lists a directory's contents by path needs this same strip, not just the quick/inline windows.
    public static void RemoveQueriedDirectoryItself(List<SearchResult>? fileResults, string query)
    {
        if (fileResults == null)
            return;

        var normalizedQuery = GetQueriedDirectoryNormalized(query);
        if (normalizedQuery == null)
            return;

        fileResults.RemoveAll(x => string.Equals(SearchResultHelper.NormalizePath(x.Path), normalizedQuery, StringComparison.OrdinalIgnoreCase));
    }

    // Single-result variant for streaming callers (e.g. AppSearchPipeService's per-item pipe callback)
    // that can't buffer into a list to RemoveAll from -- same rule, applied inline before a result is
    // ever handed to the caller.
    public static bool IsQueriedDirectoryItself(string path, string query)
    {
        var normalizedQuery = GetQueriedDirectoryNormalized(query);
        return normalizedQuery != null && string.Equals(SearchResultHelper.NormalizePath(path), normalizedQuery, StringComparison.OrdinalIgnoreCase);
    }

    // Same rule again, but resolved ONCE for a caller that then tests many results against it.
    // IsQueriedDirectoryItself re-derives this per call, and deriving it can hit the disk
    // (Directory.Exists) -- fine for the handful of results a streaming pipe callback sees, ruinous for
    // a caller filtering hundreds of thousands. Returns null when the query isn't a directory path at
    // all, which is the common case and means there is nothing to strip.
    internal static string? GetQueriedDirectory(string query) => GetQueriedDirectoryNormalized(query);

    internal static bool IsQueriedDirectory(string path, string? normalizedQueriedDirectory) =>
        normalizedQueriedDirectory != null &&
        string.Equals(SearchResultHelper.NormalizePath(path), normalizedQueriedDirectory, StringComparison.OrdinalIgnoreCase);

    private static string? GetQueriedDirectoryNormalized(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return null;
        try
        {
            var trimmed = query.Trim();
            var endsWithSeparator = trimmed.EndsWith("\\") || trimmed.EndsWith("/");
            if (trimmed.EndsWith(":\\") || trimmed.EndsWith(":/") ||
                (endsWithSeparator && (WslPath.IsPath(trimmed) || Directory.Exists(trimmed))))
                return SearchResultHelper.NormalizePath(trimmed);
        }
        catch { }

        return null;
    }
}
