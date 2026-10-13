using Lertaro.Core;
using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Services;

namespace Lertaro.App.ViewModels.Search;

/// <summary>The answer each launch source last gave, held briefly so a burst of summons loads once.</summary>
/// <remarks>
/// Asking a source is the expensive half of showing the panel: Windows Recent reads a folder and resolves
/// every shortcut in it, History probes the disk once per entry, and Recent Files is an IPC round trip.
/// None of that changes between two summons a second apart, so the answer is kept -- and dropped the
/// moment something says it should be. That is what keeps the window short: saving settings, a plugin's
/// own config page, and a history record this app just made all invalidate it.
///
/// ponytail: a plain TTL rather than a dependency graph across every setting that could feed a source. At
/// worst a tile is this stale, and the lists where that could actually be noticed are the invalidated ones.
/// </remarks>
internal static class QuickLaunchSourceCache
{
    /// <summary>How long an answer stands without anything invalidating it.</summary>
    private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(10);

    private static readonly object Gate = new();
    private static readonly Dictionary<string, (IReadOnlyList<ISearchResult> Items, long ExpiresAt)> Entries =
        new(StringComparer.OrdinalIgnoreCase);

    // A source's list changes when the settings that configure it change, or when this app records an
    // open of its own. Both are events already, so neither needs a TTL short enough to cover it.
    static QuickLaunchSourceCache()
    {
        PluginSettingsService.SettingChanged += (_, _) => Invalidate();
        SearchHistoryStore.Changed += Invalidate;
    }

    public static void Invalidate()
    {
        lock (Gate)
            Entries.Clear();
    }

    public static bool TryGet(string id, out IReadOnlyList<ISearchResult> items)
        => TryGet(id, Environment.TickCount64, out items);

    /// <summary>Same lookup against an explicit clock, so the lifetime can be tested without waiting.</summary>
    internal static bool TryGet(string id, long now, out IReadOnlyList<ISearchResult> items)
    {
        lock (Gate)
        {
            if (Entries.TryGetValue(id, out var entry) && entry.ExpiresAt > now)
            {
                items = entry.Items;
                return true;
            }

            Entries.Remove(id);
            items = Array.Empty<ISearchResult>();
            return false;
        }
    }

    public static void Set(string id, IReadOnlyList<ISearchResult> items) => Set(id, items, Environment.TickCount64);

    internal static void Set(string id, IReadOnlyList<ISearchResult> items, long now)
    {
        lock (Gate)
            Entries[id] = (items, now + (long)Lifetime.TotalMilliseconds);
    }
}
