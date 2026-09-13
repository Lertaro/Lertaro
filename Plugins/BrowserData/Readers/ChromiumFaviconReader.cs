using System.IO;
using Microsoft.Data.Sqlite;

namespace Lertaro.Plugins.BrowserData.Readers;

// Reads Chromium's separate Favicons database directly in immutable read-only mode. The Bookmarks
// JSON file has no icon payload, so this is the only local source for icons belonging to bookmarks.
internal static class ChromiumFaviconReader
{
    private const int MaxIconBytes = 256 * 1024;

    public static Dictionary<string, byte[]> Read(string profileDir)
    {
        var sourcePath = Path.Combine(profileDir, "Favicons");
        if (!File.Exists(sourcePath))
            return new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var result = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            var connectionString = $"Data Source=file:///{sourcePath.Replace('\\', '/')}?immutable=1;Mode=ReadOnly;Pooling=false";
            using var conn = new SqliteConnection(connectionString);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT m.page_url, b.image_data
                FROM icon_mapping m
                JOIN favicon_bitmaps b ON b.icon_id = m.icon_id
                WHERE m.page_url LIKE 'http%' AND b.image_data IS NOT NULL
                ORDER BY m.page_url, b.width DESC, b.height DESC
                """;
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                if (reader.IsDBNull(0) || reader.IsDBNull(1))
                    continue;

                var pageUrl = reader.GetString(0);
                var imageData = reader.GetFieldValue<byte[]>(1);
                if (string.IsNullOrWhiteSpace(pageUrl) || imageData.Length == 0 || imageData.Length > MaxIconBytes)
                    continue;

                result.TryAdd(pageUrl, imageData);
            }

            return result;
        }
        catch (Exception ex)
        {
            PluginSdk.Logger.Log($"[BrowserData] Failed to read '{sourcePath}': {ex.Message}", PluginSdk.LogLevel.Warn);
            return new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        }
    }
}
