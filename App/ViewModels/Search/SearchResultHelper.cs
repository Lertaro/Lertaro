using System.IO;
using Lertaro.Core;
using Lertaro.App.Services;
using Lertaro.PluginSdk.Services;

namespace Lertaro.App.ViewModels.Search;

internal static class SearchResultHelper
{
    public static HistoryEntryKind HistoryKindOf(AppSearchResult result) =>
        result.IsApplication ? HistoryEntryKind.Application : result.IsDir ? HistoryEntryKind.Folder : HistoryEntryKind.File;

    public static void AddSectionHeader(List<AppSearchResult> uiResults, string title, string query) => uiResults.Add(new AppSearchResult
    {
        Name = title,
        FullPath = "__SECTION_HEADER__",
        ParentDir = string.Empty,
        IsDir = false,
        Drive = string.Empty,
        ResultKind = "SectionHeader",
        Index = uiResults.Count,
        SearchQuery = query
    });

    // The row keeps the record and derives Name/FullPath/ParentDir/ContextDirectory/Drive/Metadata from
    // it when something asks. Copying them out here is what used to cost 353 bytes of strings per row --
    // held for the life of the search, on six hundred thousand rows the grid never realizes. See
    // AppSearchResult.
    public static AppSearchResult CreateUiResult(SearchResult item, string query, int index, bool isApplication, string? scope)
        => AppSearchResult.FromIndexResult(item, query, index, isApplication, scope);

    public static AppSearchResult CreateNoResultsResult(string query) => new AppSearchResult
    {
        Name = TranslationManager.Instance["Search_NoResult"],
        FullPath = "__NO_RESULTS__",
        ParentDir = string.Empty,
        IsDir = false,
        Drive = string.Empty,
        ResultKind = "Empty",
        Index = 0,
        SearchQuery = string.Empty
    };

    // Shown instead of CreateNoResultsResult when a per-type trigger (SearchResultTypePriority) was
    // typed with nothing after it yet -- "no results" would be misleading there, since no search has
    // actually run yet at all.
    public static AppSearchResult CreateResultTypeTriggerPromptResult(string typeDisplayName) => new AppSearchResult
    {
        Name = string.Format(TranslationManager.Instance["Search_ResultTypeTriggerPrompt"], typeDisplayName),
        FullPath = "__NO_RESULTS__",
        ParentDir = string.Empty,
        IsDir = false,
        Drive = string.Empty,
        ResultKind = "Empty",
        Index = 0,
        SearchQuery = string.Empty
    };

    // Same idea as CreateResultTypeTriggerPromptResult, generalized for every OTHER "operator typed,
    // no keyword after it yet" case -- a bare "*" (bypass exclusion rules) or a token-only query like
    // "::foo" both strip down to an empty clean query with no type to name, so this has no {0}.
    public static AppSearchResult CreateKeepTypingPromptResult() => new AppSearchResult
    {
        Name = TranslationManager.Instance["Search_KeepTypingPrompt"],
        FullPath = "__NO_RESULTS__",
        ParentDir = string.Empty,
        IsDir = false,
        Drive = string.Empty,
        ResultKind = "Empty",
        Index = 0,
        SearchQuery = string.Empty
    };

    public static string GetParentDisplayText(SearchResult item, bool isApplication, string? scope)
    {
        var parentDir = Path.GetDirectoryName(item.Path);
        if (isApplication)
        {
            return string.IsNullOrWhiteSpace(parentDir)
                ? TranslationManager.Instance["Search_ResultApp"]
                : string.Format(TranslationManager.Instance["Search_ResultAppDir"], parentDir);
        }

        if (!string.IsNullOrWhiteSpace(scope) && !string.IsNullOrWhiteSpace(parentDir))
        {
            return FormatRelativeParentPath(parentDir, scope);
        }

        var path = parentDir ?? string.Empty;
        return FormatWslPath(path);
    }

    public static string FormatWslPath(string path)
    {
        if (string.IsNullOrEmpty(path)) return path;
        if (path.StartsWith(@"\\wsl$\", StringComparison.OrdinalIgnoreCase))
        {
            var suffix = path.Substring(@"\\wsl$\".Length).Replace('\\', '/');
            var firstSlash = suffix.IndexOf('/');
            return firstSlash < 0 ? $"WSL-{suffix}:/" : $"WSL-{suffix.Substring(0, firstSlash)}:{suffix.Substring(firstSlash)}";
        }
        return path;
    }

    public static string FormatRelativeParentPath(string parentDir, string scope)
    {
        var relativePath = Path.GetRelativePath(scope, parentDir);
        if (string.IsNullOrWhiteSpace(relativePath) || relativePath == ".")
        {
            return string.Empty;
        }

        return relativePath.StartsWith(".\\", StringComparison.Ordinal)
            ? relativePath[2..]
            : relativePath;
    }

    public static string NormalizePath(string path)
    {
        // Indexed WSL paths are already absolute. Keep normalization lexical so displaying or comparing
        // a result cannot ask Windows to expand a short-name-looking segment and wake the distro.
        var normalized = WslPath.IsPath(path)
            ? path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
            : Path.GetFullPath(path);
        return normalized.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    public static bool IsPathInsideScope(string normalizedPath, string normalizedScope) => normalizedPath.StartsWith(normalizedScope + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || normalizedPath.StartsWith(normalizedScope + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    public static void AddShowMoreResult(List<AppSearchResult> uiResults, string query) => uiResults.Add(new AppSearchResult
    {
        Name = string.Format(TranslationManager.Instance["Search_ShowMoreTitle"], query),
        FullPath = "__SHOW_MORE__",
        ParentDir = TranslationManager.Instance["Search_ShowMoreDesc"],
        IsDir = false,
        Drive = "",
        ResultKind = "Action",
        Index = uiResults.Count,
        SearchQuery = query
    });

    public static string FormatSearchStatus(int appCount, int fileCount)
    {
        if (appCount > 0 && fileCount > 0)
        {
            return string.Format(TranslationManager.Instance["Search_StatsAppsAndFiles"], appCount, fileCount);
        }

        if (appCount > 0)
        {
            return string.Format(TranslationManager.Instance["Search_StatsAppsOnly"], appCount);
        }

        return string.Format(TranslationManager.Instance["Search_StatsFilesOnly"], fileCount);
    }
}
