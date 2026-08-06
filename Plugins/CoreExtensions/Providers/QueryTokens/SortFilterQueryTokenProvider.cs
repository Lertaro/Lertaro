using System.IO;
using System.Text.RegularExpressions;
using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.CoreExtensions.Providers.QueryTokens;

// Built-in reference implementation of the "<keyword> :[SCMAF]" (sort by Size/Created/Modified/
// Accessed, or filter by Folder) and ".ext.ext2" (extension filter) query suffix tokens.
public class SortFilterQueryTokenProvider : IQueryTokenProvider
{
    private static readonly Regex SortTokenPattern = new(@"^-?[SCMAF]-?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public string Name => TranslationService.Get("CoreExtensions_QueryTokenProvider_Name");

    public bool CanHandle(string token) => IsFilterToken(token) || SortTokenPattern.IsMatch(token);

    public Task<IReadOnlyList<ISearchResult>> ApplyAsync(string token, IReadOnlyList<ISearchResult> results) =>
        Task.FromResult(IsFilterToken(token) ? ApplyFilter(token, results) : ApplyToken(token, results));

    private static bool IsFilterToken(string token) => token.Length > 1 && token[0] == '.';

    private static IReadOnlyList<ISearchResult> ApplyFilter(string token, IReadOnlyList<ISearchResult> results)
    {
        var extensions = token.Split('.', StringSplitOptions.RemoveEmptyEntries)
            .Select(e => e.ToLowerInvariant())
            .ToHashSet();
        if (extensions.Count == 0)
            return results;

        return results.Where(r => !r.IsDir && extensions.Contains(Path.GetExtension(r.FullPath).TrimStart('.').ToLowerInvariant())).ToList();
    }

    private static IReadOnlyList<ISearchResult> ApplyToken(string token, IReadOnlyList<ISearchResult> results)
    {
        var hasDash = token[0] == '-' || token[^1] == '-';
        var letter = char.ToUpperInvariant(token.Trim('-')[0]);

        if (letter == 'F')
        {
            return results.Where(r => r.IsDir != hasDash).ToList();
        }

        // Already known from the index via ISearchResult.Metadata for every real file result -- no
        // more batch metadata lookup/IPC round trip (see FileSizeFilterProvider/DateModifiedFilterProvider
        // for the same change). A result with no real metadata (not file-index-backed) sorts using
        // Metadata's own default (0 / DateTime.MinValue), matching the old per-path lookup miss fallback.
        Func<ISearchResult, IComparable> keySelector = letter switch
        {
            'S' => r => r.Metadata.Size,
            'C' => r => r.Metadata.Created,
            'M' => r => r.Metadata.Modified,
            'A' => r => r.Metadata.Accessed,
            _ => r => r.Name
        };

        var ordered = hasDash ? results.OrderByDescending(keySelector) : results.OrderBy(keySelector);
        return ordered.ToList();
    }
}
