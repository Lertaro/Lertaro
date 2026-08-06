using Lertaro.PluginSdk.Abstractions;

using Lertaro.App.Services.Plugin;
namespace Lertaro.App.ViewModels.Search;

// Dispatches the raw tokens split out of a query's trailing ":a,b,c" suffix to whichever registered
// IQueryTokenProvider plugin claims each one, chaining the result through providers in token order.
// Operates purely on the file/directory subset the caller hands it -- has no idea about (and doesn't
// try to reconstruct) section headers, instant results, applications, or anything else that ends up in
// the final UI list; composing the final result set around whatever this returns, and deciding what a
// zero-length result means for the UI, is entirely the caller's job.
internal static class QueryTokenDispatcher
{
    public static async Task<List<AppSearchResult>> ApplyAsync(IReadOnlyList<AppSearchResult> fileResults, IReadOnlyList<string> tokens)
    {
        if (tokens.Count == 0)
            return fileResults as List<AppSearchResult> ?? fileResults.ToList();

        IReadOnlyList<ISearchResult> current = fileResults;
        List<string>? extraHighlightTerms = null;
        foreach (var token in tokens)
        {
            var provider = PluginManager.Instance.QueryTokenProviders.FirstOrDefault(p => p.CanHandle(token));
            if (provider == null)
                // An unclaimed token reads as a typo'd/unsupported filter -- silently showing the
                // un-narrowed file/directory results would look like it worked when it didn't, so the
                // whole set is dropped. The caller decides what zero file/directory results means for
                // the rest of the UI (a "no results" placeholder, an unaffected instant-result row, ...).
                return new List<AppSearchResult>();

            current = await provider.ApplyAsync(token, current);

            var highlightText = provider.GetHighlightText(token);
            if (!string.IsNullOrWhiteSpace(highlightText))
                (extraHighlightTerms ??= new List<string>()).Add(highlightText);
        }

        var results = current.Cast<AppSearchResult>().ToList();

        // A token that fuzzy-matches a path segment (e.g. "::rena") kept these results for a reason
        // beyond the main keyword -- fold its pattern into what TextHighlighter lights up too, so that
        // reason is visible, not just why the primary keyword matched.
        if (extraHighlightTerms != null)
        {
            var suffix = " " + string.Join(" ", extraHighlightTerms);
            foreach (var result in results)
                result.SearchQuery += suffix;
        }

        return results;
    }
}
