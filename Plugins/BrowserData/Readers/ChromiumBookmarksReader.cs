using System.Text.Json;

namespace Lertaro.Plugins.BrowserData.Readers;

// Chrome/Edge/Brave-family "Bookmarks" file: plain JSON, never locked by the running browser, safe to
// read directly. Structure is a "roots" object (bookmark_bar/other/synced/...), each a tree of
// {type:"folder", children:[...]} and {type:"url", name, url} nodes.
internal static class ChromiumBookmarksReader
{
    public static List<BrowserEntry> Read(string profileDir)
    {
        var path = Path.Combine(profileDir, "Bookmarks");
        var doc = TryParse(path);
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
            results.Add(new BrowserEntry(string.IsNullOrWhiteSpace(name) ? url : name, url, IsBookmark: true, SortKey: results.Count));
            return;
        }

        if (node.TryGetProperty("children", out var children) && children.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in children.EnumerateArray())
                Walk(child, results);
        }
    }
}
