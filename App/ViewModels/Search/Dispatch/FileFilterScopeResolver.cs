using Lertaro.Core.Services.Search;
using Lertaro.PluginSdk.Abstractions.Plugins;

using Lertaro.App.Services.Plugin;

namespace Lertaro.App.ViewModels.Search.Dispatch;

// The directive an activated file-filter scope hands to the engine: search ONLY inside these folders
// (engine-side directoryFilter per folder, already filtered down to index-covered ones), keeping only
// files whose name matches the filter pattern (directories always pass). Public only because it
// appears in SearchResultMapper's public signature; it is an App-internal dispatch concept.
public sealed record FileFilterScopeDirective(IReadOnlyList<string> Folders, string FilterPattern);

// Resolves the leading-keyword scope syntax ("tf report" -> search "report" inside the folders the
// "tf" filter configures) on behalf of SearchDispatchController -- the replacement for the old
// FileFilter_ ResultKind routing that materialized every scoped file as a searchable item. The
// keyword activates only when a first token is followed by a space; a keyword with no term after it
// still activates (the caller then shows its keep-typing prompt instead of searching).
internal static class FileFilterScopeResolver
{
    public static FileFilterScopeDirective? Resolve(string query, out string remainder)
    {
        var scopes = new Dictionary<string, SearchScope>(StringComparer.OrdinalIgnoreCase);
        foreach (var provider in PluginManager.Instance.SearchScopeProviders)
        {
            foreach (var scope in provider.GetSearchScopes() ?? Array.Empty<SearchScope>())
            {
                var keyword = scope.Keyword?.Trim() ?? string.Empty;
                // First registration wins; folders/pattern validation happens in Match.
                if (keyword.Length > 0 && !scopes.ContainsKey(keyword))
                    scopes[keyword] = scope;
            }
        }

        return Match(query, scopes, SearchScopeCoverage.IsIndexed, out remainder);
    }

    // Pure matching core, kept free of PluginManager/disk so tests can pin the activation rules:
    // first token must hit a registered keyword case-insensitively; the rest of the query (trimmed)
    // is the searched term; a scope whose folders are all index-uncovered does not activate at all,
    // one with partial coverage keeps only the covered folders.
    internal static FileFilterScopeDirective? Match(
        string query,
        IReadOnlyDictionary<string, SearchScope> scopes,
        Func<string, bool> isFolderIndexed,
        out string remainder)
    {
        remainder = query;
        if (string.IsNullOrEmpty(query))
            return null;

        var spaceIndex = query.IndexOf(' ');
        if (spaceIndex <= 0)
            return null;

        var keyword = query[..spaceIndex].Trim();
        if (keyword.Length == 0 || !scopes.TryGetValue(keyword, out var scope))
            return null;

        var folders = (scope.Folders ?? Array.Empty<string>())
            .Select(f => f?.Trim() ?? string.Empty)
            .Where(f => f.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(isFolderIndexed)
            .ToList();
        if (folders.Count == 0)
            return null;

        remainder = query[(spaceIndex + 1)..].Trim();
        return new FileFilterScopeDirective(folders, string.IsNullOrWhiteSpace(scope.FilterPattern) ? "*" : scope.FilterPattern);
    }
}
