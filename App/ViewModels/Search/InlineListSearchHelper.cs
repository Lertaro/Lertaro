using Lertaro.App.Services;

using Lertaro.App.ViewModels.Search.Mapping;
namespace Lertaro.App.ViewModels.Search;

// Builds the inline window's list out of the SAME global results the quick window shows, re-ordered so
// the window's own folder comes first. That ordering is the whole of "inline search" beyond the global
// search: current folder (direct children, then deeper) before everything else.
//
// It used to be fed by a second, directory-scoped engine search as well. That search cost the same as the
// global one -- the engine scans a drive's whole unique-name table before applying any directory filter,
// so scoping does not make it cheaper -- and it had to be waited for, which is why the inline window
// lagged the quick window. The global results already contain the folder's descendants; this just puts
// them where the user expects to see them.
internal static class InlineListSearchHelper
{
    public static List<AppSearchResult> MergeLocalMatches(
        List<AppSearchResult> uiResults,
        List<AppSearchResult> localMatches,
        string query,
        string? contextDirectory = null)
    {
        var combinedResults = new List<AppSearchResult>();
        var instantItems = new List<AppSearchResult>();
        var globalItems = new List<AppSearchResult>();
        var passedHeader = false;
        var searchHeaderTitle = TranslationManager.Instance["Search_SectionHeader"];

        foreach (var item in uiResults)
        {
            if (!passedHeader)
            {
                if (item.ResultKind == "SectionHeader" && item.Name == searchHeaderTitle)
                {
                    passedHeader = true;
                    continue;
                }
                if (item.IsInstantResult || item.IsPluginSearchAction || item.ResultKind == "SectionHeader")
                {
                    instantItems.Add(item);
                }
                else
                {
                    passedHeader = true;
                    globalItems.Add(item);
                }
            }
            else
            {
                globalItems.Add(item);
            }
        }

        combinedResults.AddRange(instantItems);

        var normalizedDirectory = string.IsNullOrEmpty(contextDirectory)
            ? null
            : DirectoryProximity.Normalize(contextDirectory);

        // One path appears at most once. The direct listing and the global search both cover the folder's
        // own files, and the direct listing is what guarantees they are present, so it claims the path
        // first -- a global row equal to one it produced is the same file and is dropped here.
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Direct children of the folder, from the guaranteed listing. Tier 0 by construction: the listing
        // is exactly one level.
        var currentFolder = new List<(AppSearchResult Result, int Tier)>();
        foreach (var row in localMatches)
        {
            if (seenPaths.Add(SearchResultHelper.NormalizePath(row.FullPath)))
                currentFolder.Add((row, 0));
        }

        // Everything else the global search returned, split by where it sits. A row under the folder keeps
        // its depth as its tier, so "subfolder" files still come before anything outside the folder -- the
        // three levels the user sees are current folder, its subfolders, then the rest of the drive.
        var outside = new List<AppSearchResult>();
        foreach (var row in globalItems)
        {
            if (!seenPaths.Add(SearchResultHelper.NormalizePath(row.FullPath)))
                continue;

            var tier = normalizedDirectory == null
                ? DirectoryProximity.Outside
                : DirectoryProximity.Tier(row.FullPath, normalizedDirectory);

            if (tier == DirectoryProximity.Outside)
                outside.Add(row);
            else
                currentFolder.Add((row, tier));
        }

        // Guarded the same way the "Global Search" header below is -- an empty "Current Folder" section
        // with nothing under it is misleading on its own, and (since a SectionHeader isn't an "ordinary"
        // File/Application row) it would also survive a query-token filter that finds nothing, leaving a
        // header with no results and no "no results" placeholder either.
        if (currentFolder.Count > 0)
        {
            SearchResultMapper.AddSectionHeader(combinedResults, TranslationManager.Instance["Search_LocalFolderHeader"], query);
            // OrderBy is stable, so rows sharing a tier keep the global search's own rank order, and the
            // direct listing's order is kept for tier 0.
            combinedResults.AddRange(currentFolder.OrderBy(entry => entry.Tier).Select(entry => entry.Result));
        }

        if (outside.Count > 0)
        {
            SearchResultMapper.AddSectionHeader(combinedResults, TranslationManager.Instance["Search_GlobalSearchHeader"], query);
            combinedResults.AddRange(outside);
        }

        for (var idx = 0; idx < combinedResults.Count; idx++)
        {
            combinedResults[idx].Index = idx;
        }
        return combinedResults;
    }
}
