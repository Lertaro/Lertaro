using Lertaro.App.Services;

using Lertaro.App.ViewModels.Search.Mapping;
namespace Lertaro.App.ViewModels.Search;

internal static class InlineListSearchHelper
{
    public static List<AppSearchResult> MergeLocalMatches(
        List<AppSearchResult> uiResults,
        List<AppSearchResult> localMatches,
        string query)
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
        // Guarded the same way the "Global Search" header below is -- an empty "Current Folder"
        // section with nothing under it is misleading on its own, and (since a SectionHeader isn't an
        // "ordinary" File/Application row) it would also survive a query-token filter that finds
        // nothing, leaving a header with no results and no "no results" placeholder either.
        if (localMatches.Count > 0)
        {
            SearchResultMapper.AddSectionHeader(combinedResults, TranslationManager.Instance["Search_LocalFolderHeader"], query);
            combinedResults.AddRange(localMatches);
        }

        // Global Search runs unscoped (see SearchDispatchController's inline-context null scope), so
        // it can legitimately re-match a file that's already shown above under Current Folder -- drop
        // those rather than showing the same result twice.
        var localPaths = new HashSet<string>(
            localMatches.Select(x => SearchResultHelper.NormalizePath(x.FullPath)),
            StringComparer.OrdinalIgnoreCase);
        globalItems.RemoveAll(x => localPaths.Contains(SearchResultHelper.NormalizePath(x.FullPath)));

        if (globalItems.Count > 0)
        {
            SearchResultMapper.AddSectionHeader(combinedResults, TranslationManager.Instance["Search_GlobalSearchHeader"], query);
            combinedResults.AddRange(globalItems);
        }

        for (var idx = 0; idx < combinedResults.Count; idx++)
        {
            combinedResults[idx].Index = idx;
        }
        return combinedResults;
    }
}
