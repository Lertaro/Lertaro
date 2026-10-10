using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.Core;

using Lertaro.App.Services.Plugin;
namespace Lertaro.App.ViewModels.Search;

// Dispatches the raw tokens QueryTokenScanner lifted out of a query -- any word starting with a token
// trigger, wherever it sits -- to whichever registered IQueryTokenProvider plugin claims each one,
// chaining the result through providers in token order. Each token is passed exactly as it appeared,
// trigger character included, which is why a provider's own configured prefix has to match the app-wide
// one to ever be asked (see QueryTokenPrefixRules).
// Operates purely on the file/directory subset the caller hands it -- has no idea about (and doesn't
// try to reconstruct) section headers, instant results, applications, or anything else that ends up in
// the final UI list; composing the final result set around whatever this returns, and deciding what a
// zero-length result means for the UI, is entirely the caller's job.
internal static class QueryTokenDispatcher
{
    public static async Task<List<AppSearchResult>> ApplyAsync(IReadOnlyList<AppSearchResult> fileResults, IReadOnlyList<string> tokens, CancellationToken cancellationToken = default, IEnumerable<IQueryTokenProvider>? providers = null)
    {
        if (tokens.Count == 0)
            return fileResults as List<AppSearchResult> ?? fileResults.ToList();

        // Injectable so the failure paths below are testable without the plugin registry singleton; the
        // production callers always take the registered providers.
        var candidates = providers ?? PluginManager.Instance.QueryTokenProviders;

        IReadOnlyList<ISearchResult> current = fileResults;
        List<string>? extraHighlightTerms = null;
        foreach (var token in tokens)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IQueryTokenProvider? provider = null;
            foreach (var candidate in candidates)
            {
                try
                {
                    if (PluginPerformanceMonitor.Measure(candidate, () => candidate.CanHandle(token)))
                    {
                        provider = candidate;
                        break;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log($"[QueryTokenDispatcher] Provider '{candidate.GetType().Name}' failed CanHandle: {ex.Message}", LogLevel.Error);
                }
            }

            if (provider == null)
                return new List<AppSearchResult>();

            IReadOnlyList<ISearchResult>? transformed;
            try
            {
                transformed = await PluginPerformanceMonitor.MeasureAsync(provider,
                    () => provider.ApplyAsync(token, current, cancellationToken));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Logger.Log($"[QueryTokenDispatcher] Provider '{provider.GetType().Name}' failed ApplyAsync: {ex.Message}", LogLevel.Error);
                return new List<AppSearchResult>();
            }

            if (transformed == null || transformed.Any(result => result is not AppSearchResult))
            {
                Logger.Log($"[QueryTokenDispatcher] Provider '{provider.GetType().Name}' returned an invalid result list.", LogLevel.Error);
                return new List<AppSearchResult>();
            }
            current = transformed;

            string? highlightText;
            try
            {
                highlightText = PluginPerformanceMonitor.Measure(provider, () => provider.GetHighlightText(token));
            }
            catch (Exception ex)
            {
                Logger.Log($"[QueryTokenDispatcher] Provider '{provider.GetType().Name}' failed GetHighlightText: {ex.Message}", LogLevel.Error);
                highlightText = null;
            }
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
