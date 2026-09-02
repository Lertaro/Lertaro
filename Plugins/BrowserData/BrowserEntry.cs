namespace Lertaro.Plugins.BrowserData;

// SortKey is a history entry's last-visit timestamp (higher = more recent, epoch/units don't matter
// since it's only ever compared to other entries from the same source) or a bookmark's insertion order.
internal readonly record struct BrowserEntry(
    string Title,
    string Url,
    bool IsBookmark,
    long SortKey,
    DateTimeOffset? VisitTime = null);

internal static class BrowserEntryFilter
{
    // Excludes internal/non-web schemes (chrome-extension://, chrome://, edge://, moz-extension://,
    // about:, file://, ...) -- an extension's popup page or a browser settings page showing up in a
    // launcher search is just noise, never something the user meant to reopen this way.
    public static bool IsHttpUrl(string url) =>
        url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
        url.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
}
