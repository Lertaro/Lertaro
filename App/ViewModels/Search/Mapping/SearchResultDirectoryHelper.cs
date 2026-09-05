using System.IO;
using Lertaro.Core;

namespace Lertaro.App.ViewModels.Search.Mapping;

// Split out to keep SearchResultMapper under the repository's per-file line limit. This helper owns
// only the repeated exact-directory-query checks used by buffered and streaming result paths.
internal static class SearchResultDirectoryHelper
{
    public static void RemoveQueriedDirectoryItself(List<SearchResult>? fileResults, string query)
    {
        if (fileResults == null)
            return;

        var normalizedQuery = GetQueriedDirectoryNormalized(query);
        if (normalizedQuery == null)
            return;

        fileResults.RemoveAll(x => string.Equals(SearchResultHelper.NormalizePath(x.Path), normalizedQuery, StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsQueriedDirectoryItself(string path, string query)
    {
        var normalizedQuery = GetQueriedDirectoryNormalized(query);
        return normalizedQuery != null && string.Equals(SearchResultHelper.NormalizePath(path), normalizedQuery, StringComparison.OrdinalIgnoreCase);
    }

    public static string? GetQueriedDirectory(string query) => GetQueriedDirectoryNormalized(query);

    public static bool IsQueriedDirectory(string path, string? normalizedQueriedDirectory) =>
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
