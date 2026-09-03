using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.Translator;

public sealed class TranslationInstantProvider : IInstantResultProvider
{
    private const string PluginId = "Lertaro.Plugins.Translator";
    private const string DefaultTrigger = "tr";
    private const string TranslateIcon = "M12.87 15.07l-2.54-2.51.03-.03c1.74-1.94 2.98-4.17 3.71-6.53h2.93V4h-7V2H8v2H1v2h11.17c-.68 1.95-1.75 3.79-3.17 5.41-1.02-1.13-1.86-2.37-2.51-3.7H4.48a16.4 16.4 0 0 0 3.13 5.21l-5.09 5.03L3.93 19.36 9 14.34l3.16 3.16.71-2.43zM18.5 10h-2L12 22h2l1.12-3h4.25l1.13 3h2l-4-12zm-2.63 7 1.37-3.67L18.63 17h-2.76z";

    private static readonly TimeSpan DebounceDelay = TimeSpan.FromMilliseconds(150);
    // A failed request is cached only briefly, so the same input is not re-fetched on every refresh
    // of the query -- but a transient network error must not pin the input as failed forever. After
    // the lifetime below the failure entry becomes invisible and the next keystroke retries.
    private static readonly TimeSpan FailureCacheLifetime = TimeSpan.FromSeconds(30);
    // ponytail: FIFO eviction keeps the cache bounded without an LRU structure; entries are small and
    // the natural turnover (one per distinct input) makes a proper LRU upgrade an easy follow-up.
    private const int MaxCacheEntries = 256;
    private static readonly Dictionary<string, TranslationCacheEntry> Cache = new(StringComparer.Ordinal);
    // Insertion order for FIFO eviction, plus a node map so an expired entry (or a re-added key) can be
    // removed from the order in O(1). A plain Queue<string> cannot remove a key, so an expired key that
    // got re-fetched on the next keystroke accumulated one stale queue entry per retry forever.
    private static readonly LinkedList<string> CacheInsertionOrder = new();
    private static readonly Dictionary<string, LinkedListNode<string>> CacheInsertionNodes = new(StringComparer.Ordinal);
    private static readonly HashSet<string> PendingRequests = new(StringComparer.Ordinal);
    private static string? _cachedTrigger;
    private static string? _latestRequestKey;

    static TranslationInstantProvider() => PluginSettingsService.SettingChanged += (pluginId, _) =>
    {
        if (string.Equals(pluginId, PluginId, StringComparison.OrdinalIgnoreCase))
            _cachedTrigger = null;
    };

    public string Name => TranslationService.Get("Translator_ProviderName");
    public string Description => TranslationService.Get("Translator_ProviderDesc");

    public IEnumerable<InstantResultItem> GetInstantResults(string query)
    {
        var trigger = GetTriggerPrefix();
        if (string.IsNullOrEmpty(query) || !query.StartsWith(trigger, StringComparison.OrdinalIgnoreCase))
            yield break;

        var parsed = TranslationQueryParser.Parse(query[trigger.Length..], TranslationService.GetCurrentCulture());
        var text = parsed.Text;
        if (text.Length == 0)
        {
            yield return CreateItem(TranslationService.Get("Translator_PlaceholderTitle"), TranslationService.Get("Translator_PlaceholderDesc"), "None");
            yield break;
        }

        var targetLanguage = parsed.TargetLanguage;
        var key = targetLanguage + "\n" + text;
        _latestRequestKey = key;
        if (TryGetCached(key, out var cached))
        {
            if (cached.Translation is { } translation)
            {
                var detectedLanguage = string.IsNullOrWhiteSpace(translation.DetectedLanguage)
                    ? TranslationService.Get("Translator_UnknownLanguage")
                    : translation.DetectedLanguage;
                var translatedTo = string.IsNullOrWhiteSpace(translation.TargetLanguage)
                    ? TranslationService.Get("Translator_UnknownLanguage")
                    : translation.TargetLanguage;
                var description = TranslationService.Format("Translator_DetectedLanguage", detectedLanguage)
                                  + " · "
                                  + TranslationService.Format("Translator_TargetLanguage", translatedTo);
                yield return CreateItem(translation.Text, description, "Copy", translation.Text);
            }
            else
            {
                yield return CreateItem(TranslationService.Get("Translator_FailedTitle"), TranslationService.Get("Translator_FailedDesc"), "None");
            }
            yield break;
        }

        EnsureFetchStarted(key, text, targetLanguage, trigger, query);
        yield return CreateItem(TranslationService.Get("Translator_LoadingTitle"), TranslationService.Get("Translator_LoadingDesc"), "None");
    }

    private static InstantResultItem CreateItem(string title, string description, string actionType, string actionArgument = "") => new()
    {
        Title = title,
        Description = description,
        IconData = TranslateIcon,
        IconColor = "DefaultPluginIconColor",
        ActionType = actionType,
        ActionArgument = actionArgument
    };

    private static string GetTriggerPrefix()
    {
        _cachedTrigger ??= PluginSettingsService.GetSetting(PluginId, "TranslationTrigger", DefaultTrigger).Trim();
        return (_cachedTrigger.Length > 0 ? _cachedTrigger : DefaultTrigger) + " ";
    }

    private static bool TryGetCached(string key, out TranslationCacheEntry entry)
    {
        lock (Cache)
        {
            if (!Cache.TryGetValue(key, out entry))
                return false;

            if (entry.Translation == null && DateTimeOffset.UtcNow - entry.CachedAtUtc >= FailureCacheLifetime)
            {
                Cache.Remove(key);
                if (CacheInsertionNodes.Remove(key, out var node))
                    CacheInsertionOrder.Remove(node);
                return false;
            }
            return true;
        }
    }

    private static void EnsureFetchStarted(string key, string text, string targetLanguage, string trigger, string requestQuery)
    {
        lock (PendingRequests)
        {
            if (!PendingRequests.Add(key))
                return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(DebounceDelay);
                if (!string.Equals(_latestRequestKey, key, StringComparison.Ordinal))
                    return;

                TranslationResponse? translation = null;
                try
                {
                    translation = await MicrosoftTranslationFetcher.TranslateAsync(text, targetLanguage);
                }
                catch
                {
                    // translation stays null: cached as a short-lived failure (see FailureCacheLifetime).
                }

                lock (Cache)
                {
                    if (CacheInsertionNodes.TryGetValue(key, out var existingNode))
                        CacheInsertionOrder.Remove(existingNode);
                    CacheInsertionNodes[key] = CacheInsertionOrder.AddLast(key);
                    Cache[key] = new TranslationCacheEntry(translation, DateTimeOffset.UtcNow);

                    while (Cache.Count > MaxCacheEntries && CacheInsertionOrder.First is { } oldestNode)
                    {
                        CacheInsertionOrder.RemoveFirst();
                        CacheInsertionNodes.Remove(oldestNode.Value);
                        Cache.Remove(oldestNode.Value);
                    }
                }
            }
            finally
            {
                lock (PendingRequests)
                    PendingRequests.Remove(key);
            }

            SearchRefreshService.RefreshIfMatches(currentQuery =>
                currentQuery.StartsWith(trigger, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(currentQuery, requestQuery, StringComparison.Ordinal));
        });
    }

    private readonly record struct TranslationCacheEntry(TranslationResponse? Translation, DateTimeOffset CachedAtUtc);
}
