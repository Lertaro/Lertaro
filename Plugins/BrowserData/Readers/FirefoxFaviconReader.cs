using System.IO;
using Microsoft.Data.Sqlite;

namespace Lertaro.Plugins.BrowserData.Readers;

// Firefox has used more than one favicon schema over its lifetime. Probe the known local schemas in
// order and keep the database read-only and direct, just like FirefoxPlacesReader does for places data.
internal static class FirefoxFaviconReader
{
    private const int MaxIconBytes = 256 * 1024;

    public static Dictionary<string, byte[]> Read(string profileDir)
    {
        var sourcePath = Path.Combine(profileDir, "favicons.sqlite");
        if (!File.Exists(sourcePath))
            sourcePath = Path.Combine(profileDir, "places.sqlite");
        if (!File.Exists(sourcePath))
            return new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var result = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            var connectionString = $"Data Source=file:///{sourcePath.Replace('\\', '/')}?immutable=1;Mode=ReadOnly;Pooling=false";
            using var conn = new SqliteConnection(connectionString);
            conn.Open();

            TryRead(conn, result, """
                SELECT p.page_url, i.data
                FROM moz_pages_w_icons AS p
                JOIN moz_icons_to_pages AS m ON m.page_id = p.id
                JOIN moz_icons AS i ON i.id = m.icon_id
                WHERE p.page_url LIKE 'http%' AND i.data IS NOT NULL
                ORDER BY p.page_url,
                    CASE WHEN i.width BETWEEN 16 AND 512 THEN 0 ELSE 1 END,
                    i.width DESC
                """);
            TryRead(conn, result, """
                SELECT p.url, f.data
                FROM moz_places AS p
                JOIN moz_favicons AS f ON f.id = p.favicon_id
                WHERE p.url LIKE 'http%' AND f.data IS NOT NULL
                """);
            return result;
        }
        catch (Exception ex)
        {
            PluginSdk.Logger.Log($"[BrowserData] Failed to read Firefox favicons from '{sourcePath}': {ex.Message}", PluginSdk.LogLevel.Warn);
            return new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static void TryRead(SqliteConnection conn, Dictionary<string, byte[]> result, string query)
    {
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = query;
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
        }
        catch (SqliteException)
        {
            // Firefox schema variants are expected; an absent table or column just selects the next query.
        }
    }
}
