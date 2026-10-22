using System.Text.Json;
using System.Text.RegularExpressions;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.WebSearch;

internal static class WebSearchSuggestionFetcher
{
    private static readonly TimeSpan SuggestionDebounce = TimeSpan.FromMilliseconds(200);

    // The keys these endpoints put the text under, tried in this order: 哔哩哔哩's items carry both a
    // clean "value" and a "name" wrapped in the highlight markup, so "value" has to come first.
    private static readonly string[] SuggestionTextKeys = ["value", "q", "word", "term", "keyword", "suggestion", "query", "text", "name"];

    private const int MaxSearchDepth = 8;
    private const int MaxSuggestionLength = 200;

    // The highlight markup these endpoints wrap the matched part in (哔哩哔哩: <em
    // class="suggest_high_light">) never reaches a row, and each suggestion stays on one line.
    private static readonly Regex SuggestionMarkup = new(@"<[^>]{0,64}>", RegexOptions.Compiled);
    private static readonly Regex SuggestionWhitespace = new(@"\s+", RegexOptions.Compiled);

    private static string Tidy(string text) => SuggestionWhitespace.Replace(SuggestionMarkup.Replace(text, string.Empty), " ").Trim();

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
        return ParseSuggestions(json, searchTerm);
    }

    internal static List<string> ParseSuggestions(string json, string typed)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // OpenSearch's ["what was typed", [suggestions]] has to be split apart before the generic scan,
        // which would otherwise read the typed word itself as the first suggestion.
        var isPairOfTypedWordAndSuggestions = root.ValueKind == JsonValueKind.Array
            && root.GetArrayLength() >= 2 && root[0].ValueKind == JsonValueKind.String && root[1].ValueKind == JsonValueKind.Array;
        var found = isPairOfTypedWordAndSuggestions ? TextList(root[1]) : FirstTextList(root);

        var result = new List<string>();
        foreach (var raw in found)
        {
            var text = Tidy(raw);
            if (text.Length == 0 || text.Length > MaxSuggestionLength
                || string.Equals(text, typed, StringComparison.OrdinalIgnoreCase)
                || result.Contains(text, StringComparer.OrdinalIgnoreCase))
                continue;
            result.Add(text);
        }
        return result;
    }

    // The texts one array holds, in order, skipping the items that give none.
    private static List<string> TextList(JsonElement array)
    {
        var texts = new List<string>();
        foreach (var item in array.EnumerateArray())
            if (ItemText(item) is { Length: > 0 } text)
                texts.Add(text);
        return texts;
    }

    // The first array, depth first, that holds any text: whatever wrappers a site puts around its list,
    // 哔哩哔哩's result.tag and api.bilibili.com's data.result.tag among them.
    private static List<string> FirstTextList(JsonElement element, int depth = 0)
    {
        var none = new List<string>();
        if (depth > MaxSearchDepth)
            return none;

        if (element.ValueKind == JsonValueKind.Array)
        {
            var texts = TextList(element);
            if (texts.Count > 0)
                return texts;
            foreach (var item in element.EnumerateArray())
                if (FirstTextList(item, depth + 1) is { Count: > 0 } found)
                    return found;
        }
        else if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
                if (FirstTextList(property.Value, depth + 1) is { Count: > 0 } found)
                    return found;
        }
        return none;
    }

    // One item of a suggestion list as its text: a string, the first string of an array (淘宝's
    // [["词","数"]]), or the first known key of an object.
    private static string? ItemText(JsonElement item)
    {
        if (item.ValueKind == JsonValueKind.String)
            return item.GetString();
        if (item.ValueKind == JsonValueKind.Array)
            return item.GetArrayLength() > 0 && item[0].ValueKind == JsonValueKind.String ? item[0].GetString() : null;
        if (item.ValueKind != JsonValueKind.Object)
            return null;

        foreach (var key in SuggestionTextKeys)
            if (item.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String)
                return value.GetString();
        return null;
    }

    // Re-triggers active searches so they pick up newly-cached suggestions, via the host-provided
    // SearchRefreshService rather than reflecting into concrete App-side view model types.
    private static void RefreshActiveSearches(string prefix, string searchTerm) => SearchRefreshService.RefreshIfMatches(currentQueryText =>
                                                                                            currentQueryText.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                                                                                            string.Equals(currentQueryText.Substring(prefix.Length).Trim(), searchTerm, StringComparison.OrdinalIgnoreCase));
}
