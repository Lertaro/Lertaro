using Lertaro.Core.Services.Search;
using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Services;

using Lertaro.App.Services.Plugin;

namespace Lertaro.App.ViewModels.Search.Dispatch;

// The directive an activated file-filter scope hands to the engine: search ONLY inside these folders
// (engine-side directoryFilter per folder, already filtered down to index-covered ones), keeping only
// files whose name matches the filter pattern (directories always pass). Public only because it
// appears in SearchResultMapper's public signature; it is an App-internal dispatch concept.
public sealed record FileFilterScopeDirective(IReadOnlyList<string> Folders, string FilterPattern);

// Resolves the leading-keyword scope syntax ("tf report" -> search "report" inside the folders the
// "tf" filter configures) on behalf of SearchDispatchController -- the replacement for the old
// FileFilter_ ResultKind routing that materialized every scoped file as a searchable item. The keyword
// activates only once a separator has been typed after it, unlike a search action, which activates bare
// too; a keyword with no term after it still activates (the caller then shows its keep-typing prompt
// instead of searching).
internal static class FileFilterScopeResolver
{
    public static FileFilterScopeDirective? Resolve(string query, out string remainder)
    {
        var scopes = new Dictionary<string, SearchScope>(StringComparer.OrdinalIgnoreCase);
        foreach (var provider in PluginManager.Instance.SearchScopeProviders)
        {
            foreach (var scope in provider.GetSearchScopes() ?? Array.Empty<SearchScope>())
            {
                var keyword = TriggerWord.Normalize(scope.Keyword);
                // First registration wins; folders/pattern validation happens in Match.
                if (keyword.Length > 0 && !scopes.ContainsKey(keyword))
                    scopes[keyword] = scope;
            }
        }

        return Match(query, scopes, SearchScopeCoverage.IsIndexed, out remainder);
    }

    // Pure matching core, kept free of PluginManager/disk so tests can pin the activation rules: a typed
    // first token must hit a registered keyword case-insensitively; the rest of the query (trimmed) is
    // the searched term; a scope whose folders are all index-uncovered does not activate at all, one with
    // partial coverage keeps only the covered folders.
    //
    // Scanning the registered keywords instead of looking one up costs nothing here and keeps the tokenizer
    // single-sourced: a match needs the first token to EQUAL a keyword, so no two distinct keywords can
    // both claim a query and the scan order cannot change the answer.
    internal static FileFilterScopeDirective? Match(
        string query,
        IReadOnlyDictionary<string, SearchScope> scopes,
        Func<string, bool> isFolderIndexed,
        out string remainder)
    {
        remainder = query;
        if (string.IsNullOrEmpty(query))
            return null;

        foreach (var (keyword, scope) in scopes)
        {
            // "tf" alone is still a legitimate search for the text "tf" -- the scope needs something typed
            // after the keyword before it commits, which is where it differs from a bare command word.
            if (!TriggerWord.TryMatchInvoked(query, keyword, out var term))
                continue;

            var folders = (scope.Folders ?? Array.Empty<string>())
                .Select(f => f?.Trim() ?? string.Empty)
                .Where(f => f.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(isFolderIndexed)
                .ToList();
            if (folders.Count == 0)
                return null;

            remainder = term;
            return new FileFilterScopeDirective(folders, string.IsNullOrWhiteSpace(scope.FilterPattern) ? "*" : scope.FilterPattern);
        }

        return null;
    }
}
