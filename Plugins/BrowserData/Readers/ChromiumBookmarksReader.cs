using System.IO;
using System.Text.Json;

namespace Lertaro.Plugins.BrowserData.Readers;

// Chrome/Edge/Brave-family bookmark files: plain JSON, never locked by the running browser, safe to read
// directly. Structure is a "roots" object (bookmark_bar/other/synced/...), each a tree of
// {type:"folder", children:[...]} and {type:"url", name, url} nodes -- the same shape in the local
// Bookmarks store and in the signed-in account's AccountBookmarks store, and in the .bak of either.
internal static class ChromiumBookmarksReader
{
    // The bookmark stores a profile can keep, tried in this order: the first one that actually carries a
    // tree wins and nothing after it is opened.
    //
    // AccountBookmarks leads because that is where a signed-in Edge/Chrome keeps the account's own
    // bookmarks, which is a different set from the local Bookmarks file. Each live file is followed by the
    // .bak the browser leaves behind when it rewrites it -- the copy that survives a crashed or
    // interrupted write.
    //
    // A candidate that parses but carries no "roots" is not a bookmarks file, so it yields to the next
    // rather than stopping the walk: stopping there would let one empty stub hide the profile's real
    // bookmarks.
    private static readonly string[] CandidateFileNames =
    [
        "AccountBookmarks", "Bookmarks", "AccountBookmarks.bak", "Bookmarks.bak",
    ];

    public static List<BrowserEntry> Read(string profileDir)
    {
        foreach (var name in CandidateFileNames)
        {
            var doc = TryParse(Path.Combine(profileDir, name));
            if (doc == null)
                continue;

            using (doc)
            {
                if (!doc.RootElement.TryGetProperty("roots", out var roots))
                    continue;

                var results = new List<BrowserEntry>();
                foreach (var root in roots.EnumerateObject())
                    Walk(root.Value, results);
                return results;
            }
        }

        return new List<BrowserEntry>();
    }

    private static JsonDocument? TryParse(string path)
    {
        if (!File.Exists(path))
            return null;
        try
        {
            using var stream = File.OpenRead(path);
            return JsonDocument.Parse(stream);
        }
        catch (Exception ex)
        {
            PluginSdk.Logger.Log($"[BrowserData] Failed to parse bookmarks file '{path}': {ex.Message}", PluginSdk.LogLevel.Warn);
            return null;
        }
    }

    private static void Walk(JsonElement node, List<BrowserEntry> results)
    {
        if (node.ValueKind != JsonValueKind.Object)
            return;

        var type = node.TryGetProperty("type", out var t) ? t.GetString() : null;
        if (type == "url")
        {
            var url = node.TryGetProperty("url", out var u) ? u.GetString() : null;
            if (string.IsNullOrWhiteSpace(url) || !BrowserEntryFilter.IsHttpUrl(url))
                return;
            var name = node.TryGetProperty("name", out var n) ? n.GetString() : null;
            results.Add(new BrowserEntry(string.IsNullOrWhiteSpace(name) ? url : name, url, isBookmark: true, sortKey: results.Count));
            return;
        }

        if (node.TryGetProperty("children", out var children) && children.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in children.EnumerateArray())
                Walk(child, results);
        }
    }
}
