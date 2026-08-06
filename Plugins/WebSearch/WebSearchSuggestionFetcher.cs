using System.Text.Json;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.WebSearch;

internal static class WebSearchSuggestionFetcher
{
    private static readonly TimeSpan SuggestionDebounce = TimeSpan.FromMilliseconds(200);

    private static readonly HttpClient SuggestionHttpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
    private static readonly HashSet<string> PendingSuggestionRequests = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, List<string>> SuggestionCache = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
    private static string? _latestSuggestionRequestKey;

    static WebSearchSuggestionFetcher()
    {
        try
        {
            // Some suggestion endpoints (e.g. Wikipedia's) reject requests with no User-Agent header.
            SuggestionHttpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36 Edg/125.0.0.0");
        }
        catch { }
    }

    public static List<string>? GetCached(string suggestionKey)
    {
        _latestSuggestionRequestKey = suggestionKey;
        lock (SuggestionCache)
        {
            SuggestionCache.TryGetValue(suggestionKey, out var suggestions);
            return suggestions;
        }
    }

    public static void EnsureFetchStarted(WebSearchInstantProvider.SearchSourceItem source, string searchTerm, string suggestionKey, string prefix)
    {
        var shouldTrigger = false;
        lock (PendingSuggestionRequests)
        {
            if (!PendingSuggestionRequests.Contains(suggestionKey))
            {
                PendingSuggestionRequests.Add(suggestionKey);
                shouldTrigger = true;
            }
        }

        if (shouldTrigger)
        {
            TriggerSuggestionFetch(source, searchTerm, suggestionKey, prefix);
        }
    }

    private static void TriggerSuggestionFetch(WebSearchInstantProvider.SearchSourceItem source, string searchTerm, string suggestionKey, string prefix) => Task.Run(async () =>
                                                                                                                                    {
                                                                                                                                        var fetched = false;
                                                                                                                                        try
                                                                                                                                        {
                                                                                                                                            await Task.Delay(SuggestionDebounce);
                                                                                                                                            if (_latestSuggestionRequestKey != suggestionKey)
                                                                                                                                            {
                                                                                                                                                // The user has already moved on to a different query; skip the network call.
                                                                                                                                                return;
                                                                                                                                            }

                                                                                                                                            var suggestions = await FetchSuggestionsAsync(source.SuggestUrl, searchTerm);
                                                                                                                                            lock (SuggestionCache)
                                                                                                                                            {
                                                                                                                                                SuggestionCache[suggestionKey] = suggestions;
                                                                                                                                            }
                                                                                                                                            fetched = true;
                                                                                                                                        }
                                                                                                                                        catch
                                                                                                                                        {
                                                                                                                                            lock (SuggestionCache)
                                                                                                                                            {
                                                                                                                                                SuggestionCache[suggestionKey] = new List<string>();
                                                                                                                                            }
                                                                                                                                            fetched = true;
                                                                                                                                        }
                                                                                                                                        finally
                                                                                                                                        {
                                                                                                                                            lock (PendingSuggestionRequests)
                                                                                                                                            {
                                                                                                                                                PendingSuggestionRequests.Remove(suggestionKey);
                                                                                                                                            }
                                                                                                                                        }

                                                                                                                                        if (fetched)
                                                                                                                                        {
                                                                                                                                            RefreshActiveSearches(prefix, searchTerm);
                                                                                                                                        }
                                                                                                                                    });

    private static async Task<List<string>> FetchSuggestionsAsync(string suggestUrlTemplate, string searchTerm)
    {
        var url = WebSearchInstantProvider.BuildUrl(suggestUrlTemplate, searchTerm);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);

        var cultureName = TranslationService.GetCurrentCulture();
        if (!string.IsNullOrWhiteSpace(cultureName))
        {
            try
            {
                request.Headers.AcceptLanguage.Add(new System.Net.Http.Headers.StringWithQualityHeaderValue(cultureName));
            }
            catch { }
        }

        using var response = await SuggestionHttpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        return ParseOpenSearchSuggestions(json);
    }

    private static List<string> ParseOpenSearchSuggestions(string json)
    {
        var result = new List<string>();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() < 2)
            return result;

        var suggestionsElement = root[1];
        if (suggestionsElement.ValueKind != JsonValueKind.Array)
            return result;

        foreach (var item in suggestionsElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
                continue;

            var text = item.GetString();
            if (!string.IsNullOrWhiteSpace(text))
                result.Add(text);
        }
        return result;
    }

    // Re-triggers active searches so they pick up newly-cached suggestions, via the host-provided
    // SearchRefreshService rather than reflecting into concrete App-side view model types.
    private static void RefreshActiveSearches(string prefix, string searchTerm) => SearchRefreshService.RefreshIfMatches(currentQueryText =>
                                                                                            currentQueryText.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                                                                                            string.Equals(currentQueryText.Substring(prefix.Length).Trim(), searchTerm, StringComparison.OrdinalIgnoreCase));
}
