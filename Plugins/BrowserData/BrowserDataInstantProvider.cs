using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.BrowserData;

public class BrowserDataInstantProvider : IInstantResultProvider
{
    // PluginLoader instantiates this once (via Activator.CreateInstance) as soon as it discovers
    // IInstantResultProvider while scanning Plugins/ at app startup -- kicking off the cache's
    // background reload right here means the first real "bm <query>" of the session doesn't land on
    // a still-empty snapshot. BrowserDataCache.Preload() reuses the same staleness-guarded reload path
    // GetSnapshot() already uses, so this is safe even if something ever constructs a second instance.
    public BrowserDataInstantProvider() => BrowserDataCache.Preload();

    public string Name => TranslationService.Get("BrowserData_ProviderName");

    // Bounds how many rows this ever hands back per keystroke -- irrelevant of how many entries
    // matched, only the best few are worth showing alongside every other result source.
    private const int MaxResults = 8;

    // Cap for the "just the trigger keyword, no search term" case (mirrors ProcessManager's own
    // Take(100) for "ps" alone) -- there's no narrowing term competing for attention from other
    // result sources at that point, so a much larger list is worth showing than the per-keystroke cap.
    private const int MaxAllResults = 100;

    // Used only when a profile's own configured Icon is left empty in settings.
    private const string DefaultBookmarkIcon = "M17 3H7c-1.1 0-2 .9-2 2v16l7-3 7 3V5c0-1.1-.9-2-2-2z";
    private const string DefaultHistoryIcon = "M13 3a9 9 0 0 0-9 9H1l3.89 3.89.07.14L9 12H6c0-3.87 3.13-7 7-7s7 3.13 7 7-3.13 7-7 7c-1.93 0-3.68-.79-4.94-2.06l-1.42 1.42A8.954 8.954 0 0 0 13 21a9 9 0 0 0 0-18zm-1 5v5l4.28 2.54.72-1.21-3.5-2.08V8H12z";

    // Falls back to the default even if an empty string was already persisted before RequireNonEmpty
    // started enforcing this at save time -- an empty keyword should never silently make this
    // unreachable.
    private static string GetTriggerKeyword()
    {
        var value = PluginSettingsService.GetSetting("Lertaro.Plugins.BrowserData", "TriggerKeyword", "bm");
        return string.IsNullOrWhiteSpace(value) ? "bm" : value;
    }

    // Requires an explicit "<keyword>" or "<keyword> <search term>" prefix (mirrors ProcessManager's
    // bare "ps"/"ps <term>") rather than matching on every query -- a large cache of bookmarks/history
    // would otherwise compete for attention against every unrelated search, and scanning it is real
    // per-keystroke cost (see CollectMatches) that shouldn't be paid unless the user actually asked
    // for a browser-data search. The bare-keyword case (searchTerm stays "") is what lets
    // GetInstantResults list everything instead of never even being reached.
    private static bool TryStripKeyword(string query, out string searchTerm)
    {
        searchTerm = string.Empty;
        var keyword = GetTriggerKeyword();
        if (string.IsNullOrWhiteSpace(keyword))
            return false;

        var trimmed = query.Trim();
        if (string.Equals(trimmed, keyword, StringComparison.OrdinalIgnoreCase))
            return true;

        if (!trimmed.StartsWith(keyword + " ", StringComparison.OrdinalIgnoreCase))
            return false;

        searchTerm = trimmed.Substring(keyword.Length + 1).Trim();
        return true;
    }

    public IEnumerable<InstantResultItem> GetInstantResults(string query)
    {
        if (string.IsNullOrWhiteSpace(query) || !TryStripKeyword(query, out var q))
            return Array.Empty<InstantResultItem>();

        // A single character matches almost anything in a large history and is expensive to fully
        // scan for very little payoff -- same tradeoff already established for Core's own file search.
        // An EMPTY remaining term (just "bm" alone) is deliberately not blocked here, unlike length 1:
        // it lists everything instead of matching nothing, mirroring ProcessManager's "ps" alone.
        if (q.Length == 1)
            return Array.Empty<InstantResultItem>();

        var snapshot = BrowserDataCache.GetSnapshot();
        if (snapshot.Count == 0)
            return Array.Empty<InstantResultItem>();

        var matches = new List<(BrowserEntry Entry, ProfileEntries Profile, int Tier)>();
        foreach (var profile in snapshot)
        {
            CollectMatches(profile.Bookmarks, profile, q, matches);
            CollectMatches(profile.History, profile, q, matches);
        }

        var limit = q.Length == 0 ? MaxAllResults : MaxResults;
        return matches
            .OrderByDescending(m => m.Entry.IsBookmark)
            .ThenBy(m => m.Tier)
            .ThenByDescending(m => m.Entry.SortKey)
            .Take(limit)
            .Select(m => ToInstantResult(m.Entry, m.Profile))
            .ToList();
    }

    public bool[]? GetHighlightMask(string text, string query)
    {
        if (string.IsNullOrEmpty(query) || !TryStripKeyword(query, out var searchTerm) || searchTerm.Length == 0)
            return null;

        // Same literal/fuzzy/alias tiers (including CJK pinyin) the host's own results highlight with --
        // a plain literal IndexOf here would leave a title matched only through CollectMatches' fuzzy
        // fallback (e.g. a Chinese title matched via pinyin initials) completely unhighlighted.
        return FuzzyMatchService.GetHighlightMask(text, searchTerm) ?? new bool[text.Length];
    }

    // Cheap literal substring check first (no fzf-pattern parsing) covers the common case of typing a
    // recognizable piece of a title/URL -- only entries that don't literally contain the query anywhere
    // pay for the more expensive fuzzy-match fallback below. With a couple of profiles' worth of
    // bookmarks + up to a few thousand history entries scanned on every keystroke, this tiering keeps
    // the common case fast instead of running the fuzzy matcher over the whole cache every time.
    private static void CollectMatches(List<BrowserEntry> entries, ProfileEntries profile, string query, List<(BrowserEntry, ProfileEntries, int)> matches)
    {
        // Empty query ("bm" alone): everything matches, same tier -- skip the Contains/fuzzy checks
        // below entirely rather than let them all trivially match, since Contains("", ...) is a
        // needless scan and FuzzyMatchService.IsMatch isn't designed for an empty pattern anyway.
        if (query.Length == 0)
        {
            foreach (var entry in entries)
                matches.Add((entry, profile, 0));
            return;
        }

        foreach (var entry in entries)
        {
            if (entry.Title.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                matches.Add((entry, profile, 0));
                continue;
            }
            if (entry.Url.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                matches.Add((entry, profile, 1));
                continue;
            }
            if (FuzzyMatchService.IsMatch(query, entry.Title))
            {
                matches.Add((entry, profile, 2));
            }
        }
    }

    private static InstantResultItem ToInstantResult(BrowserEntry entry, ProfileEntries profile)
    {
        var iconData = !string.IsNullOrWhiteSpace(profile.Profile.Icon)
            ? profile.Profile.Icon
            : entry.IsBookmark ? DefaultBookmarkIcon : DefaultHistoryIcon;

        var descriptionPrefix = !string.IsNullOrWhiteSpace(profile.Profile.Name) ? $"{profile.Profile.Name} · " : string.Empty;

        var description = descriptionPrefix + entry.Url;
        if (!entry.IsBookmark && entry.VisitTime is { } visitTime)
            description = $"{BrowserHistoryTime.Format(visitTime)} · {description}";

        return new InstantResultItem
        {
            Title = entry.Title,
            Description = description,
            IconData = iconData,
            IconColor = "AccentBlue",
            ActionType = "Execute",
            ActionArgument = entry.Url,
            TabCompletion = entry.Url
        };
    }
}
