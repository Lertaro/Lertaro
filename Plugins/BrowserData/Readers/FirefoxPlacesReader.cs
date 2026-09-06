using Microsoft.Data.Sqlite;

namespace Lertaro.Plugins.BrowserData.Readers;

// Firefox-family "places.sqlite": bookmarks and history both live in the same database.
// moz_bookmarks (type=1 rows are actual bookmarks, fk -> moz_places.id) joined with moz_places for the
// URL/title; moz_places itself doubles as the history table via last_visit_date.
internal static class FirefoxPlacesReader
{
    private const int MaxHistoryEntries = 2000;

    public static (List<BrowserEntry> Bookmarks, List<BrowserEntry> History) Read(string profileDir)
    {
        var sourcePath = Path.Combine(profileDir, "places.sqlite");
        if (!File.Exists(sourcePath))
            return (new List<BrowserEntry>(), new List<BrowserEntry>());

        try
        {
            var bookmarks = new List<BrowserEntry>();
            var history = new List<BrowserEntry>();

            // immutable=1 bypasses SQLite's OS file-locking layer (LockFileEx), allowing direct
            // read-only queries against live browser files without colliding with Firefox's active locks
            // or copying tens of megabytes to %TEMP% (eliminating SSD write amplification).
            var connectionString = $"Data Source=file:///{sourcePath.Replace('\\', '/')}?immutable=1;Mode=ReadOnly;Pooling=false";
            using var conn = new SqliteConnection(connectionString);
            conn.Open();

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"SELECT p.url, COALESCE(NULLIF(b.title, ''), p.title, p.url)
                                     FROM moz_bookmarks b JOIN moz_places p ON b.fk = p.id
                                     WHERE b.type = 1 AND p.url IS NOT NULL AND p.url LIKE 'http%'";
                using var reader = cmd.ExecuteReader();
                var order = 0;
                while (reader.Read())
                {
                    if (reader.IsDBNull(0))
                        continue;
                    var url = reader.GetString(0);
                    if (string.IsNullOrWhiteSpace(url) || !BrowserEntryFilter.IsHttpUrl(url))
                        continue;
                    var title = reader.IsDBNull(1) ? url : reader.GetString(1);
                    bookmarks.Add(new BrowserEntry(
                        string.IsNullOrWhiteSpace(title) ? url : title,
                        url,
                        isBookmark: true,
                        sortKey: order++,
                        family: BrowserFamily.Firefox));
                }
            }

            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"SELECT url, title, last_visit_date FROM moz_places
                                     WHERE last_visit_date IS NOT NULL AND hidden = 0 AND url LIKE 'http%'
                                     ORDER BY last_visit_date DESC LIMIT $limit";
                cmd.Parameters.AddWithValue("$limit", MaxHistoryEntries);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    if (reader.IsDBNull(0))
                        continue;
                    var url = reader.GetString(0);
                    if (string.IsNullOrWhiteSpace(url) || !BrowserEntryFilter.IsHttpUrl(url))
                        continue;
                    var title = reader.IsDBNull(1) ? url : reader.GetString(1);
                    var lastVisit = reader.IsDBNull(2) ? 0L : reader.GetInt64(2);
                    history.Add(new BrowserEntry(
                        string.IsNullOrWhiteSpace(title) ? url : title,
                        url,
                        isBookmark: false,
                        sortKey: lastVisit,
                        family: BrowserFamily.Firefox));
                }
            }

            return (bookmarks, history);
        }
        catch (Exception ex)
        {
            PluginSdk.Logger.Log($"[BrowserData] Failed to read '{sourcePath}': {ex.Message}", PluginSdk.LogLevel.Warn);
            return (new List<BrowserEntry>(), new List<BrowserEntry>());
        }
    }
}
