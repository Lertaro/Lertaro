using Lertaro.Core;
using Lertaro.Core.SearchIndex;
using Lertaro.App.ViewModels.Search.Dispatch;
using Lertaro.App.Services.Plugin;
using Lertaro.PluginSdk.Abstractions.Plugins;

using SearchWindowType = Lertaro.PluginSdk.Abstractions.SearchWindowType;

namespace Lertaro.App.ViewModels.Search.Mapping;

public static class SearchResultMapper
{
    /// <summary>
    /// One pass over the instant-result providers and the search-action plugins, kept for the rest of a
    /// search so it runs once per keystroke instead of once per paint.
    /// </summary>
    /// <remarks>
    /// Filled on first use, and by whoever asks, so no caller has to know whether someone already paid
    /// for it: <see cref="BuildQuickResults"/> takes an optional one and a caller that passes nothing keeps
    /// the old build-one-for-this-call behaviour. Rows are shared across paints on purpose -- the
    /// streaming accumulator has handed the same row instances to successive renders all along, and
    /// SearchResultsReconciler is built to keep a row whose content matches.
    ///
    /// What this is for: the pass reaches every instant provider in the process, including the ones that
    /// answer by enumerating live system state (open windows and their thumbnails, running processes,
    /// audio endpoints) and the ones that answer from a database. On a search that streams, doing that per
    /// paint costs seconds and, worse, gates the file rows behind it -- the paint cannot show the index
    /// matches until every provider has answered again.
    /// </remarks>
    internal sealed class InstantPassCache
    {
        public bool Collected;
        public readonly List<AppSearchResult> Rows = new();
        public bool HasPluginSearchActions;
    }

    internal static List<AppSearchResult> CollectInstantPass(InstantPassCache pass, string rawQuery, string query, bool isInlineWindow, string? contextDirectory,
        IEnumerable<IInstantResultProvider>? providers = null)
    {
        // The early result emission and the file render run on different workers for the same search.
        lock (pass)
        {
            if (pass.Collected)
                return pass.Rows;

            // Providers and command actions need the original trigger word; only highlighting uses the stripped query.
            PluginSearchResultMapper.AddInstantResults(pass.Rows, rawQuery, query, isInlineWindow, contextDirectory,
                providers ?? PluginManager.Instance.InstantResultProviders);
            pass.HasPluginSearchActions = PluginSearchResultMapper.AddPluginSearchActionResults(pass.Rows, rawQuery, contextDirectory, isInlineWindow);
            pass.Collected = true;
            return pass.Rows;
        }
    }

    // skipDisplayCap: token mode (SearchDispatchController.ComposeAndApplyAsync) still applies its own
    // final 50-item cap AFTER filtering by the token, but needs the FULL ranked candidate set to filter
    // over first -- capping to the usual ~50 here, before a "::xxx"/directory-segment token ever runs,
    // silently drops the token's real matches whenever they don't also happen to be in the top ~50 by
    // plain filename weight (e.g. a common substring like "1080" already fills that cap with unrelated
    // files before the directory filter gets a chance to run at all).
    // The history usage counts move only when the user opens something -- but they were rebuilt for EVERY
    // search, and that rebuild is the one-time cost that makes a repeated search feel like it hitches instead
    // of opening instantly. Held across searches for a short window instead. (Re-applied: the v5.9.6 merge
    // replaced this file and dropped it.)
    // ponytail: a 1 s lifetime, not invalidation hooks. Ceiling: a just-opened item can be a second late in
    // gaining its open count. Upgrade path: bump a version counter in SearchHistoryStore and compare here.
    private static IReadOnlyDictionary<string, int>? _recentUsage;
    private static long _recentUsageAt;
    private const long RecentPassLifetimeMs = 1000;


    private static IReadOnlyDictionary<string, int> RecentUsage()
    {
        var now = Environment.TickCount64;
        if (_recentUsage == null || now - _recentUsageAt > RecentPassLifetimeMs)
        {
            _recentUsage = SearchHistoryStore.UsageCounts();
            _recentUsageAt = now;
        }

        return _recentUsage ?? EmptyUsage;
    }

    private static readonly IReadOnlyDictionary<string, int> EmptyUsage =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

    internal static List<AppSearchResult> BuildQuickResults(List<SearchResult>? fileResults, string query, string? scope, string? contextDirectory, bool isInlineWindow, string? rawQuery = null, bool skipDisplayCap = false, FileFilterScopeDirective? fileFilterScope = null, bool folderScope = false, InstantPassCache? instantPass = null, RankContext? rankContext = null, ViewModels.Search.SearchResultCategory category = ViewModels.Search.SearchResultCategory.All)
    {
        var uiResults = new List<AppSearchResult>();
        // One pass over the plugin providers per search, not per paint. This method IS a paint callback --
        // the quick and inline windows re-run it on every intermediate paint of a streaming search, and a
        // paint cannot reach the screen until the pass below finishes. See InstantPassCache.
        instantPass ??= new InstantPassCache();
        CollectInstantPass(instantPass, rawQuery ?? query, query, isInlineWindow, contextDirectory);
        uiResults.AddRange(instantPass.Rows);
        var hasPluginSearchActions = instantPass.HasPluginSearchActions;

        RemoveQueriedDirectoryItself(fileResults, query);

        // A start-menu entry, a desktop shortcut and the program it points at are three rows for one thing.
        // Drop the shortcut when it merely spells its target's name, and move its history onto the target --
        // before ranking, so the row that survives inherits the position the shortcut had earned.
        fileResults = DedupeShortcutRows(fileResults, rawQuery ?? query, ref rankContext);

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

        // The profile folders under the name Windows shows for them. The index only knows the on-disk name
        // (Desktop, Downloads, Music), so a query in Chinese or its pinyin could not reach them even though
        // typing the English name always could. Injected after the scope filter on purpose: an inline search
        // is scoped to the folder being browsed, and these folders are precisely not inside it -- filtering
        // them afterwards is what would make them unreachable again. The row carries the display name so it
        // reads and ranks like the thing the user named, and the real path so opening it still works.
        foreach (var folder in Lertaro.App.Services.KnownFolderNames.Match(query))
        {
            // The index usually already returned this folder under its on-disk name, so injecting a second
            // row would show "Desktop" next to "桌面" for the same folder. Rename the row that is already
            // there instead, and add one only when the index did not return it at all.
            // Compared through the shared normalizer: the index stores paths without a trailing
            // separator while GetFolderPath may hand one back, and a raw string compare on the two
            // forms never matched -- so the row was neither renamed nor recognised as present.
            var existing = fileResults?.FirstOrDefault(r =>
                string.Equals(
                    SearchResultHelper.NormalizePath(r.Path),
                    SearchResultHelper.NormalizePath(folder.Path),
                    StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                existing.Name = folder.DisplayName;
                continue;
            }

            fileResults ??= new List<SearchResult>();
            fileResults.Add(new SearchResult
            {
                Name = folder.DisplayName,
                Path = folder.Path,
                IsDir = true,
                Drive = System.IO.Path.GetPathRoot(folder.Path) ?? string.Empty,
            });
        }

        // A file-filter scope needs no per-result path check: its folders were already handed to the
        // engine as the per-folder directoryFilter (see SearchStreamRenderer's fan-out), so every
        // streamed result is inside the scope by construction. What still needs scoping here are the
        // candidate tiers that bypass the engine -- history and favorites -- plus keeping general
        // searchable items (apps, settings) out of what is now a deliberately file-scoped result set.
        var normalizedScopeFolders = fileFilterScope?.Folders.Select(f => SearchResultHelper.NormalizePath(f)).ToList();
        bool InFileFilterScope(string normalizedPath) => normalizedScopeFolders != null && normalizedScopeFolders.Any(f =>
            SearchResultHelper.IsPathInsideScope(normalizedPath, f)
            && !string.Equals(normalizedPath, f, StringComparison.OrdinalIgnoreCase));

        var historyUsage = RecentUsage();

        // Quick-window-only: the user-orderable result-type order (RankedCandidate.TypeRank) is compared
        // inside each ranking section, not above it (see RankPolicy.CompareSectionRank): before match
        // quality everywhere except the low-priority section, where quality comes first. Every file-backed
        // row (index files, favorites, history, Windows' Recent) starts on FilesTypeRank so those rows stay
        // one group; a shortcut then borrows the better of that rank and the application type's (below).
        // The inline window never consults the list at all.
        var typeOrder = isInlineWindow ? new List<string>() : UserSettings.Load().ResultTypeOrder;
        var typeRanks = SearchResultTypePriority.BuildRanks(typeOrder);
        var filesTypeRank = SearchResultTypePriority.Rank(SearchResultTypePriority.FilesTypeId, typeRanks);

        // A .lnk/.appref-ms is a file row that is also an application shortcut. Taking the better of
        // the two ranks keeps the user's "Applications first" order working for shortcuts the app provider
        // never scanned (a folder that is not in 自定义应用搜索文件夹), and makes the mirrored "Files first"
        // order harmless instead of silently demoting every shortcut to the file rank. A .url keeps the
        // file rank: it points at a web page, so the application type has nothing to say about it.
        var shortcutTypeRank = SearchResultTypePriority.ShortcutRank(
            SearchResultTypePriority.RankForResultKind(
                SearchResultTypePriority.ApplicationResultKind,
                PluginManager.Instance.SearchableItemProviders,
                typeRanks),
            filesTypeRank);

        // Quick-window-only exclusive filter: if the first character the user actually typed matches a
        // configured per-type trigger (UserSettings.ResultTypeTriggers), only that type's candidates
        // enter the ranked competition below -- history and Windows Recent are skipped when a type
        // trigger is active; favorites still join the list. rawQuery (not the sort/exclusion-token-stripped
        // query) is what's probed since it's the one guaranteed to still have whatever the user actually
        // typed first, including a literal space -- see PluginSearchResultMapper.AddInstantResults' own
        // use of it above for the same reason. query itself already arrives with the trigger character
        // stripped (ResultTypeTriggerHandler.StripTrigger removes it before the file-index engine ever
        // sees it), so it's used as-is below for matching/highlighting -- the trigger character never
        // shows up highlighted and never pollutes the fuzzy match, with no second stripping needed here.
        string? triggeredTypeId = null;
        if (!isInlineWindow)
        {
            var probe = rawQuery ?? query;
            if (probe.Length > 0)
            {
                triggeredTypeId = SearchResultTypePriority.ResolveTrigger(probe[0], UserSettings.Load().ResultTypeTriggers);
                // The same precedence SearchDispatchController applies before it cuts the character off the
                // searched text: a word some plugin owns outranks the character that merely starts it, or
                // the exclusive filter below would compete a "set 路径" search on the leftover "et ". Read
                // from the raw probe, since `query` has already had that word stripped out of it here. This
                // is the quick/inline mapper, so the word inventory is the quick window's (Main).
                if (triggeredTypeId != null && PluginTriggerQuery.ClaimsLeadingWord(probe, SearchWindowType.Main))
                    triggeredTypeId = null;
            }
        }

        // Favorites, history-matched files, searchable items (apps/settings), and remaining file
        // results all compete on ONE list now: the type grouping first (one hard group per type), then,
        // inside a type, match quality, then favorites, then history band (5+ opens / 1-4 / none).
        // History never overrules a better textual match, and quality never outranks a type the user put
        // earlier.
        var candidates = new List<RankedCandidate>();

        // Parsed once for this whole result set. The file loop below runs once per file result (thousands
        // on a broad query), and the string-based ComputeMatchRank re-parsed the query on every one of
        // them -- Parse re-consults every alias provider's GetQueryForms, so with pinyin loaded that was
        // the dominant per-keystroke cost in this mapper.
        var fuzzy = FuzzyQuery.Parse(query);

        // The history entry already remembers which query opened its path. Re-introduce existing paths
        // whose learned keyword matches this query even when the index's bounded first page omitted them.
        // Inline results enter Global Search here; its separately scoped Current Folder tier wins the
        // downstream path dedupe. An explicit type trigger retains its strict result domain.
        if (triggeredTypeId == null)
        {
            var historyCandidates = HistorySearchCandidateMapper.Collect(fuzzy, scope, filesTypeRank);
            if (normalizedScopeFolders != null)
                historyCandidates = historyCandidates.Where(c => InFileFilterScope(c.NormalizedPath)).ToList();
            candidates.AddRange(historyCandidates);

            // Windows' Recent Items: what was opened in any app, ranked right below the user's own picks.
            var recentCandidates = WindowsRecentCandidateMapper.Collect(fuzzy, scope, filesTypeRank);
            if (normalizedScopeFolders != null)
                recentCandidates = recentCandidates.Where(c => InFileFilterScope(c.NormalizedPath)).ToList();
            candidates.AddRange(recentCandidates);
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            var favorites = UserSettings.Load().Favorites;
            for (var i = 0; i < favorites.Count; i++)
            {
                var fav = favorites[i];
                var match = FavoriteSearchHelper.ComputeMatch(fav, fuzzy);
                if (!match.IsMatch || !Helpers.FavoritePathResolver.IsPathAvailable(fav.Path))
                    continue;

                // A favorite is curated by the user regardless of whether it also has USAGE history.
                // Under the four-section model it competes on its own score: a strong name match makes it
                // 44, above every history tier; a weak one keeps the ordinary base score, so it cannot
                // overrule a much better textual match. The type list never applies to it, it only
                // inherits the Files rank like every other file-backed row.
                var normalizedFavPath = Helpers.FavoritePathResolver.NormalizeForComparison(fav.Path);
                if (normalizedScopeFolders != null && !InFileFilterScope(normalizedFavPath))
                    continue;
                var favoriteRow = FavoriteSearchHelper.CreateFavoriteUiResult(fav, query, 0);
                // A favorite clears the same bar the index side's rows do (see
                // HistorySearchCandidateMapper.IsExcludedFromSearch): a hidden target, or one on a source
                // this session found unreachable, is not a row the full window could list either.
                if (HistorySearchCandidateMapper.IsExcludedFromSearch(favoriteRow))
                    continue;
                var usage = historyUsage.TryGetValue(normalizedFavPath, out var uc) ? uc : 0;
                candidates.Add(new RankedCandidate(
                    favoriteRow,
                    IsFavorite: true,
                    HistoryCount: usage,
                    filesTypeRank,
                    match,
                    normalizedFavPath));
            }
        }

        // Searchable items (Start Menu apps, settings, ...) take part in ordinary searches only -- a
        // file-filter scope is by definition about files inside its folders, and the old FileFilter
        // routing hid these too (its "Case B").
        if (fileFilterScope == null)
        {
            foreach (var (result, match) in SearchableItemMapper.CollectSearchableItemResults(query, isInlineWindow))
            {
                var typeId = result.SourceProvider is ISearchableItemProvider provider
                    ? SearchResultTypePriority.GetProviderTypeId(provider)
                    : SearchResultTypePriority.FilesTypeId;
                if (triggeredTypeId != null && typeId != triggeredTypeId)
                    continue;

                // An application's FullPath can be a virtual shell:AppsFolder\{AUMID} id (packaged apps) --
                // Path.GetFullPath (inside NormalizePath) would mangle that, and SearchHistoryStore itself
                // never runs it through NormalizePath either (see SearchHistoryStore.RecordCore), so the
                // lookup key has to skip it here too or an app's history priority would never resolve.
                var lookupPath = result.IsApplication ? result.FullPath.Trim() : SearchResultHelper.NormalizePath(result.FullPath);
                var usage = historyUsage.TryGetValue(lookupPath, out var uc) ? uc : 0;
                var rowTypeRank = SearchResultTypePriority.Rank(typeId, typeRanks);

                candidates.Add(new RankedCandidate(
                    result,
                    IsFavorite: false,
                    HistoryCount: usage,
                    rowTypeRank,
                    match,
                    SearchResultHelper.NormalizePath(result.FullPath)));
            }
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
                var usage = historyUsage.TryGetValue(lookupPath, out var uc) ? uc : 0;
                // A row the index returned whose own NAME does not match the query at all -- it was reached
                // through a transliteration of that name -- ranks by how much of its whole path the query
                // explains, which is the rank the full window's row key has always given it
                // (SearchResultRelevance.Apply). Leaving it at NoMatch instead made this window order those
                // rows by path length and name where the full window ordered the same rows by coverage: one
                // query, two file orders, and exactly the many-CJK-files case pinyin search produces.
                var match = fuzzy.Rank(result.Name);
                if (!match.IsMatch)
                    match = fuzzy.FallbackRank(result.Path);
                candidates.Add(new RankedCandidate(
                    SearchResultHelper.CreateUiResult(result, query, 0, isApplication: false, scope),
                    IsFavorite: false,
                    HistoryCount: usage,
                    filesTypeRank,
                    match,
                    SearchResultHelper.NormalizePath(result.Path)));
            }
        }

        // Second identity for the rows above: every shortcut still sitting on the Files rank (index file,
        // favorite, history, Windows Recent -- the searchable-item loop already carries the app rank) takes
        // the better of the two. Nothing moves when the app type is unlisted or ranked below Files.
        ApplyShortcutTypeRank(candidates, filesTypeRank, shortcutTypeRank);

        // The history and favorite rows merged above never pass through the engine's own folder filter, so the
        // scope has to be re-applied to them here. Before ranking and before the display cap, so a dropped
        // file cannot take a row the folders would have used.
        if (folderScope)
            candidates.RemoveAll(c => !c.Result.IsDir);

        // The inline window's global tier shares this ranking, but its history caps are 1/3 while the quick
        // window's are 3/10. Mixed into one log that has no window field, the bucket distribution would tilt
        // toward Boosted, so the inline window is not sampled.
        var ordered = RankedCandidateOrdering.Order(candidates, rankContext, isInlineWindow ? null : query, typeOrder);

        // One program, one row -- across BOTH sources. The app provider contributes the installed entry while
        // the file index contributes every shortcut the user has to it, and DedupeShortcutRows only ever sees
        // the file side, so the same program arrived twice: once named by the provider and once as a shortcut.
        // Collapsing here, after the two lists have been merged and ranked, is the only place that can see both
        // and keep whichever ranked higher.
        // Unless the query is asking for shortcut files themselves. "lnk" or a name ending in ".lnk" means the
        // shortcut IS the thing being searched for, so collapsing several of them into one would hide exactly
        // what was asked for -- the same rule DedupeShortcutRows already follows on the file side.
        if (!LooksLikeShortcutQuery(rawQuery ?? query))
        {
            var seenLauncherTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            ordered = ordered.Where(row =>
            {
                var path = row.Result.FullPath;
                if (string.IsNullOrEmpty(path))
                    return true;

                if (!row.Result.IsApplication && !path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
                    return true;

                var target = Lertaro.App.Helpers.ShortcutTargetResolver.Resolve(path);
                var key = SearchResultHelper.NormalizePath(target ?? path);
                return seenLauncherTargets.Add(key);
            }).ToList();
        }

        // The category filter runs HERE, before any cap. Filtering afterwards meant the cap had already
        // thrown those rows away under the whole-result ordering: a short query ranks applications and
        // folders first, so an Audio tab received a list with no audio in it. Filtering first also makes the
        // display cap meaningful again, because it then counts rows of the type the user asked for.
        if (category != ViewModels.Search.SearchResultCategory.All)
            ordered = ordered.Where(row => ViewModels.Search.SearchResultCategorizer.Matches(category, row.Result)).ToList();
        // Capped here (not deferred to the caller) because this display cap has to respect whatever
        // header/grouping layout the caller (or InlineListSearchHelper.MergeLocalMatches, downstream)
        // builds around these rows -- e.g. the inline window's "Current Folder"/"Global Search" split
        // needs its own files to stay adjacent to its own header. SearchDispatchController only takes
        // over capping/filtering once a query token is active, since token mode collapses that
        // grouping anyway (see its own composition logic). Same two-tier shape as before (show
        // everything under 10 total, else pad to ~8 then allow up to 50 with a "N more" marker) --
        // just applied to the now-unified candidate list instead of only file results.

        // A filtered list skips the display cap entirely. The cap exists to keep the unfiltered list scrollable
        // and to hand the overflow to "show more"; applying it to a filtered list would mean any magic number
        // could hide the row the user is actually looking for, however far down the whole-result ordering it
        // sits. Inside one category there is nothing to overflow into -- the user asked for that type, so the
        // list shows that type.
        if (skipDisplayCap || category != ViewModels.Search.SearchResultCategory.All || uiResults.Count + ordered.Count < 10)
        {
            foreach (var candidate in ordered)
            {
                candidate.Result.Index = uiResults.Count;
                uiResults.Add(candidate.Result);
            }

            StampWindow(uiResults, isInlineWindow);
            return uiResults;
        }

        // How much the unfiltered list scrolls before the "show more" row takes over. A filtered list never
        // reaches here (see above), so this number no longer has to be guessed large enough to cover every
        // category -- which was the wrong shape for a limit: the row being looked for could always sit past
        // whichever value was picked.
        const int CandidatePool = 50;
        var hasMoreAtEnd = ordered.Count > CandidatePool;
        // A type the user bothered to order must not vanish behind the cap: keep its best few rows and let
        // the rest of the slots follow the global order (EnsureTypeCoverage never reorders, only selects).
        if (hasMoreAtEnd)
            ordered = RankedCandidateOrdering.EnsureTypeCoverage(ordered, CandidatePool);

        var firstCount = Math.Min(ordered.Count, Math.Max(0, 8 - uiResults.Count));
        for (var i = 0; i < firstCount; i++)
        {
            ordered[i].Result.Index = uiResults.Count;
            uiResults.Add(ordered[i].Result);
        }

        for (var i = firstCount; i < ordered.Count; i++)
        {
            ordered[i].Result.Index = uiResults.Count;
            uiResults.Add(ordered[i].Result);
        }

        if (hasMoreAtEnd)
        {
            SearchResultHelper.AddShowMoreResult(uiResults, query);
        }

        StampWindow(uiResults, isInlineWindow);
        return uiResults;
    }

    // A start-menu entry, a desktop shortcut and the program they point at are three rows for one thing:
    // the index carries the .exe, the shell's recent list carries the .lnk, the start menu carries another.
    //
    // Only a shortcut that repeats its target's own name is dropped -- "Apex.lnk" pointing at "Apex.exe".
    // A shortcut the user renamed ("我的工具.lnk" -> "Apex.exe") is a name they chose, so it stays. A query
    // that asks for shortcuts by name is left alone entirely: typing "lnk" means the extension is the thing
    // being searched for.
    //
    // The dropped row's history moves to the target before ranking, not after: ranking with the shortcut
    // still present would place the survivor using a score it never earned, and then dropping the shortcut
    // would leave that position behind.
    private static List<SearchResult>? DedupeShortcutRows(List<SearchResult>? rows, string query, ref RankContext? rankContext)
    {
        if (rows is not { Count: > 1 })
            return rows;

        if (LooksLikeShortcutQuery(query))
            return rows;

        var dropped = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // One entry per target: the same program is reachable from a Start-menu entry, a Quick Launch entry,
        // a taskbar pin and an installed-folder shortcut, and those are four rows for one thing on screen.
        // This set is what collapses them, and it has to be separate from the name test below -- that test
        // deliberately keeps a shortcut the user renamed, while these rows all carry the same name anyway.
        var seenTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, int>? merged = null;
        Dictionary<string, SearchResult>? byPath = null;
        foreach (var row in rows)
        {
            if (!row.Path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
                continue;

            byPath ??= BuildPathIndex(rows);
            var target = Lertaro.App.Helpers.ShortcutTargetResolver.Resolve(row.Path);
            if (target is null)
                continue;

            // Compare through the shared normalizer: the index stores a path without its trailing separator
            // and a shortcut's target arrives from GetFullPath, so a raw string compare on the two forms can
            // miss the row that is right there.
            var targetKey = SearchResultHelper.NormalizePath(target);

            // A second, third, fourth shortcut to the same target: drop it whatever it is called.
            if (!seenTargets.Add(targetKey))
            {
                dropped.Add(row.Path);
                MergeHistory(row.Path, targetKey, ref rankContext, ref merged);
                continue;
            }

            if (!byPath.ContainsKey(targetKey) && !byPath.ContainsKey(target))
                continue;

            // Renamed by the user: the shortcut's own name is the name to keep.
            if (!string.Equals(System.IO.Path.GetFileNameWithoutExtension(row.Path), System.IO.Path.GetFileNameWithoutExtension(target), StringComparison.OrdinalIgnoreCase))
                continue;

            dropped.Add(row.Path);
            MergeHistory(row.Path, targetKey, ref rankContext, ref merged);
        }

        if (dropped.Count == 0)
            return rows;

        if (rankContext is { } existing && merged is { Count: > 0 })
        {
            var combined = new Dictionary<string, int>(existing.HistoryScoreX100, StringComparer.OrdinalIgnoreCase);
            foreach (var (path, score) in merged)
                combined[path] = Math.Max(combined.GetValueOrDefault(path), score);
            rankContext = new RankContext(existing.PriorityResolver, combined, existing.FavoritePaths,
                existing.HistoryFiveMax, existing.HistoryThreeMax);
        }

        if (Logger.IsEnabled(LogLevel.Debug))
            Logger.Log($"[ShortcutDedupe] dropped {dropped.Count} shortcut(s): {string.Join("; ", dropped)}", LogLevel.Debug);

        return rows.FindAll(row => !dropped.Contains(row.Path));
    }

    // Moves a dropped shortcut's open count onto its target, taking the LARGER of the two rather than a sum:
    // several shortcuts to one program must not add up to a score higher than any of them ever earned.
    private static void MergeHistory(string shortcutPath, string targetKey, ref RankContext? rankContext,
        ref Dictionary<string, int>? merged)
    {
        var shortcutKey = SearchResultHelper.NormalizePath(shortcutPath);
        if (rankContext is not { } context || !context.HistoryScoreX100.TryGetValue(shortcutKey, out var score) || score <= 0)
            return;

        merged ??= new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        merged[targetKey] = Math.Max(merged.GetValueOrDefault(targetKey), score);
    }

    private static Dictionary<string, SearchResult> BuildPathIndex(List<SearchResult> rows)
    {
        var index = new Dictionary<string, SearchResult>(rows.Count * 2, StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            index[row.Path] = row;
            // Also under the normalized form, so a target compared after NormalizePath finds the row even
            // when the two spellings differ by a trailing separator.
            index[SearchResultHelper.NormalizePath(row.Path)] = row;
        }
        return index;
    }

    // Whether the query is asking for shortcut files themselves rather than for whatever they point at.
    // A bare "lnk" or a name ending in ".lnk" means the extension is the thing being searched for; the
    // letters "lnk" inside an ordinary word must not switch de-duplication off.
    private static bool LooksLikeShortcutQuery(string query)
    {
        var trimmed = query.Trim();
        if (trimmed.Length == 0)
            return false;
        if (trimmed.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
            return true;

        // Any word, not just the whole query: "xx lnk" asks for the shortcut of xx, and testing the whole string
        // missed it because the trailing word is neither equal to "lnk" nor ends in ".lnk" once the leading word
        // is part of the text. Both spellings mean the same thing here -- a bare "lnk" and a trailing ".lnk".
        foreach (var word in trimmed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (word.Equals("lnk", StringComparison.OrdinalIgnoreCase)
                || word.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }


    // IsFavorite: an explicit user pick. HistoryCount is how many times the path was opened (0 when not
    // in history), which the kernel only uses when it has no decayed score for the path. TypeRank is the
    // row's position in the user's result-type order (int.MaxValue when the type is not listed); the quick
    // window adds its type bonus to the C/P score and the display cap keeps a few rows per type, while the
    // full window has no type layer at all. IsHistoryOnly marks a row that
    // exists only because a history keyword matched: its name did not match, so the kernel must not treat
    // the keyword rank as a name match. Which keys these become is RankPolicy's decision, not this type's.
    internal readonly record struct RankedCandidate(
        AppSearchResult Result,
        bool IsFavorite,
        int HistoryCount,
        int TypeRank,
        MatchRank Match,
        string NormalizedPath,
        bool IsHistoryOnly = false);

    // A shortcut file is both a file row and an application shortcut, and it ranks as whichever identity the
    // user put first -- where the app provider never indexed it, the file row is the only row it has, so
    // without this it would carry only the Files rank (see SearchResultTypePriority.ShortcutRank). Only rows
    // still sitting on the Files rank move: an Application row already carries the better rank, a directory
    // whose name ends in ".lnk" is not a shortcut, and an .exe is a file like any other. Split out of
    // BuildQuickResults so the wiring is testable without a search engine behind it.
    internal static void ApplyShortcutTypeRank(List<RankedCandidate> candidates, int filesTypeRank, int shortcutTypeRank)
    {
        if (shortcutTypeRank >= filesTypeRank)
            return;

        for (var i = 0; i < candidates.Count; i++)
        {
            var row = candidates[i];
            var rank = SearchResultTypePriority.TypeRankForRow(
                row.TypeRank, row.Result.IsDir, row.NormalizedPath, filesTypeRank, shortcutTypeRank);
            if (rank != row.TypeRank)
                candidates[i] = row with { TypeRank = rank };
        }
    }

    // Shared by both search groups the inline window shows (its own "Current Folder" matches via
    // ExplorerSearchHelper, and this "Global Search" tier below) so a file scores the same way regardless
    // of which of the two it happens to land in. The ranking itself lives in RankPolicy; this only decides
    // which row owns a path and where each row's signals come from.
    /// <param name="query">Passed on to the calibration log when the quick window ranks; null keeps the
    /// ordering identical and records nothing.</param>
    internal static List<AppSearchResult> RankAndDedupe(List<RankedCandidate> candidates, RankContext? context = null, string? query = null)
        => RankedCandidateOrdering.Order(candidates, context, query).Select(c => c.Result).ToList();

    // The same ordering, keeping the rank alongside each row -- for callers that need to consult it again
    // after ordering (the inline window's directory-proximity pass does). It ranks a folder's own listing,
    // not a query page, so it does not feed the calibration log.
    internal static List<RankedCandidate> RankCandidates(List<RankedCandidate> candidates, RankContext? context = null)
        => RankedCandidateOrdering.Order(candidates, context);

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
        => SearchResultDirectoryHelper.RemoveQueriedDirectoryItself(fileResults, query);

    // Single-result variant for streaming callers (e.g. AppSearchPipeService's per-item pipe callback)
    // that can't buffer into a list to RemoveAll from -- same rule, applied inline before a result is
    // ever handed to the caller.
    public static bool IsQueriedDirectoryItself(string path, string query)
        => SearchResultDirectoryHelper.IsQueriedDirectoryItself(path, query);

    // Same rule again, but resolved ONCE for a caller that then tests many results against it.
    // IsQueriedDirectoryItself re-derives this per call, and deriving it can hit the disk
    // (Directory.Exists) -- tolerable one row at a time, ruinous for
    // a caller filtering hundreds of thousands. Returns null when the query isn't a directory path at
    // all, which is the common case and means there is nothing to strip.
    internal static string? GetQueriedDirectory(string query) => SearchResultDirectoryHelper.GetQueriedDirectory(query);

    internal static bool IsQueriedDirectory(string path, string? normalizedQueriedDirectory) =>
        SearchResultDirectoryHelper.IsQueriedDirectory(path, normalizedQueriedDirectory);

    // The template used to answer "is this the quick window?" itself, with a RelativeSource walk up to the
    // owning Window on three elements of every row. That is a visual-tree traversal per evaluation, and a
    // repaint evaluates it for every visible row; a measured 200-row repaint took 60-100 ms. Answering it
    // once, on the data, removes those walks entirely.
    private static void StampWindow(List<AppSearchResult> rows, bool isInlineWindow)
    {
        for (var i = 0; i < rows.Count; i++)
            rows[i].IsQuickWindow = !isInlineWindow;
    }
}
