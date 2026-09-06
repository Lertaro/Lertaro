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
    public BrowserDataInstantProvider()
    {
        if (BrowserDataCache.IsComponentEnabled)
            BrowserDataCache.Preload();
    }

    public string Name => TranslationService.Get("BrowserData_ProviderName");

    // Bounds how many rows this ever hands back per keystroke -- only the best few are worth showing
    // in the launcher result list.
    private const int MaxResults = 8;

    // Cap for the "just the trigger keyword, no search term" case (mirrors ProcessManager's own
    // Take(100) for "ps" alone) -- there's no narrowing term competing for attention from other
    // result sources at that point, so a much larger list is worth showing than the per-keystroke cap.
    private const int MaxAllResults = 100;

    // Used only when a profile's own configured Icon is left empty in settings.
    private const string DefaultBookmarkIcon = "M17 3H7c-1.1 0-2 .9-2 2v16l7-3 7 3V5c0-1.1-.9-2-2-2z";
    private const string DefaultHistoryIcon = "M13 3a9 9 0 0 0-9 9H1l3.89 3.89.07.14L9 12H6c0-3.87 3.13-7 7-7s7 3.13 7 7-3.13 7-7 7c-1.93 0-3.68-.79-4.94-2.06l-1.42 1.42A8.954 8.954 0 0 0 13 21a9 9 0 0 0 0-18zm-1 5v5l4.28 2.54.72-1.21-3.5-2.08V8H12z";

    private static string GetTriggerKeyword(string settingKey, string defaultValue)
    {
        var value = PluginSettingsService.GetSetting("Lertaro.Plugins.BrowserData", settingKey, defaultValue);
        return string.IsNullOrWhiteSpace(value) ? defaultValue : value.Trim();
    }

    public IEnumerable<InstantResultItem> GetInstantResults(string query)
    {
        var parsed = BrowserDataQueryParser.Parse(
            query,
            GetTriggerKeyword("BookmarkTriggerKeyword", "bb"),
            GetTriggerKeyword("HistoryTriggerKeyword", "bh"));
        if (!parsed.IsHandled)
            return Array.Empty<InstantResultItem>();

        var q = parsed.SearchTerm;

        var snapshot = BrowserDataCache.GetSnapshot();
        if (snapshot.Count == 0)
            return Array.Empty<InstantResultItem>();

        var matches = new List<(BrowserEntry Entry, ProfileEntries Profile, string Description, int Tier)>();
        foreach (var profile in snapshot)
        {
            var entries = parsed.Scope == BrowserDataSearchScope.Bookmarks ? profile.Bookmarks : profile.History;
            CollectMatches(entries, profile, q, matches);
        }

        var limit = q.Length == 0 ? MaxAllResults : MaxResults;
        return matches
            .OrderBy(m => m.Tier)
            .ThenByDescending(m => m.Entry.SortKey)
            .Take(limit)
            .Select(m => ToInstantResult(m.Entry, m.Profile, m.Description))
            .ToList();
    }

    public bool[]? GetHighlightMask(string text, string query)
    {
        var parsed = BrowserDataQueryParser.Parse(
            query,
            GetTriggerKeyword("BookmarkTriggerKeyword", "bb"),
            GetTriggerKeyword("HistoryTriggerKeyword", "bh"));
        if (!parsed.IsHandled || parsed.SearchTerm.Length == 0)
            return null;

        // Same literal/fuzzy/alias tiers (including CJK pinyin) the host's own results highlight with --
        // a plain literal IndexOf here would leave a title matched only through CollectMatches' fuzzy
        // fallback (e.g. a Chinese title matched via pinyin initials) completely unhighlighted.
        return FuzzyMatchService.GetHighlightMask(text, parsed.SearchTerm) ?? new bool[text.Length];
    }

    // Matches Line 1 (Title) and Line 2 (Description: Time + Source Profile + URL) simultaneously.
    // Tier 0 is exact Title literal match. Tier 1 is Title fuzzy/pinyin match. Tier 2 is full-item match
    // across both lines combined, supporting multi-word queries (e.g. "2026 09", "Chrome proxies") as well
    // as fuzzy/pinyin matching against any part of the record.
    internal static void CollectMatches(
        List<BrowserEntry> entries,
        ProfileEntries profile,
        string query,
        List<(BrowserEntry Entry, ProfileEntries Profile, string Description, int Tier)> matches)
    {
        // Empty query (a bare trigger alone): everything matches, same tier -- skip checks entirely.
        if (query.Length == 0)
        {
            foreach (var entry in entries)
                matches.Add((entry, profile, FormatDescription(entry, profile), 0));
            return;
        }

        foreach (var entry in entries)
        {
            var description = FormatDescription(entry, profile);
            var fullText = $"{entry.Title} {description}";

            if (entry.Title.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                matches.Add((entry, profile, description, 0));
                continue;
            }

            if (FuzzyMatchService.IsMatch(query, entry.Title))
            {
                matches.Add((entry, profile, description, 1));
                continue;
            }

            if (fullText.Contains(query, StringComparison.OrdinalIgnoreCase) || FuzzyMatchService.IsMatch(query, fullText))
            {
                matches.Add((entry, profile, description, 2));
            }
        }
    }

    internal static string FormatDescription(BrowserEntry entry, ProfileEntries profile)
    {
        var descriptionPrefix = !string.IsNullOrWhiteSpace(profile.Profile.Name) ? $"{profile.Profile.Name} · " : string.Empty;
        var description = descriptionPrefix + entry.Url;
        if (!entry.IsBookmark && entry.VisitTime is { } vt)
        {
            description = $"{BrowserHistoryTime.Format(vt)} · {description}";
        }

        return description;
    }

    private static InstantResultItem ToInstantResult(BrowserEntry entry, ProfileEntries profile, string description)
    {
        var iconData = !string.IsNullOrWhiteSpace(profile.Profile.Icon)
            ? profile.Profile.Icon
            : entry.IsBookmark ? DefaultBookmarkIcon : DefaultHistoryIcon;

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
