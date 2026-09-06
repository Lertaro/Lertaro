using Lertaro.Plugins.BrowserData.Readers;

namespace Lertaro.Plugins.BrowserData;

// Lightweight reference type representing a bookmark or history item. Stored as a class (not a large struct)
// so internal List<BrowserEntry> arrays hold 8-byte references, keeping them well below the 85,000-byte LOH
// threshold even for profiles with tens of thousands of items.
internal sealed class BrowserEntry
{
    public string Title { get; }
    public string Url { get; }
    public bool IsBookmark { get; }
    public long SortKey { get; }
    public BrowserFamily Family { get; }

    public DateTimeOffset? VisitTime =>
        SortKey == 0 ? null : (Family == BrowserFamily.Firefox
            ? BrowserHistoryTime.FromFirefox(SortKey)
            : BrowserHistoryTime.FromChromium(SortKey));

    public BrowserEntry(string title, string url, bool isBookmark, long sortKey, BrowserFamily family = BrowserFamily.Chromium)
    {
        Title = title;
        Url = url;
        IsBookmark = isBookmark;
        SortKey = sortKey;
        Family = family;
    }
}

internal static class BrowserEntryFilter
{
    // Excludes internal/non-web schemes (chrome-extension://, chrome://, edge://, moz-extension://,
    // about:, file://, ...) -- an extension's popup page or a browser settings page showing up in a
    // launcher search is just noise, never something the user meant to reopen this way.
    public static bool IsHttpUrl(string url) =>
        url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
        url.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
}
