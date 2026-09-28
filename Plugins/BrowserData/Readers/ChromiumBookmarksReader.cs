using System.IO;
using System.Text.Json;

namespace Lertaro.Plugins.BrowserData.Readers;

// Chrome/Edge/Brave-family "Bookmarks" file: plain JSON, never locked by the running browser, safe to
// read directly. Structure is a "roots" object (bookmark_bar/other/synced/...), each a tree of
// {type:"folder", children:[...]} and {type:"url", name, url} nodes.
internal static class ChromiumBookmarksReader
{
    // Chrome and Edge rewrite Bookmarks by first copying it to Bookmarks.bak, so the previous -- and for a
    // crashed or interrupted write, sometimes the only intact -- copy of the same tree sits one file name
    // away in the same folder. Read it when Bookmarks cannot supply a tree of its own, and never when it
    // can: the fallback is the older of the two, so preferring it would show bookmarks the user deleted.
    //
    // A Bookmarks that exists but will not parse counts as "cannot supply a tree" too, which is the case
    // the .bak is actually there for.
    public static List<BrowserEntry> Read(string profileDir)
    {
        var doc = TryParse(Path.Combine(profileDir, "Bookmarks"))
            ?? TryParse(Path.Combine(profileDir, "Bookmarks.bak"));
        if (doc == null)
            return new List<BrowserEntry>();

        using (doc)
        {
            var results = new List<BrowserEntry>();
            if (doc.RootElement.TryGetProperty("roots", out var roots))
            {
                foreach (var root in roots.EnumerateObject())
                    Walk(root.Value, results);
            }
            return results;
        }
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
