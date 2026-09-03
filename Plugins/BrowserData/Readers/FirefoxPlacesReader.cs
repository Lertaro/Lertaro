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

        // Both queries run against the same temp copy -- combined into one list here (tagged via
        // IsBookmark) since SqliteCopyReader's copy/cleanup is scoped to a single read callback.
        var combined = SqliteCopyReader.ReadCopy(sourcePath, tempPath =>
        {
            var results = new List<BrowserEntry>();

            // Pooling=false: Microsoft.Data.Sqlite's default connection pool keeps the native file
            // handle open after Dispose() in case the same connection string gets reused -- it never
            // does here (tempPath is a fresh GUID every call), so pooling only left the temp file
            // locked open by this very process, making SqliteCopyReader's own delete-after-use fail.
            using var conn = new SqliteConnection($"Data Source={tempPath};Mode=ReadOnly;Pooling=false");
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
                    results.Add(new BrowserEntry(string.IsNullOrWhiteSpace(title) ? url : title, url, IsBookmark: true, SortKey: order++));
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
                    results.Add(new BrowserEntry(string.IsNullOrWhiteSpace(title) ? url : title, url, IsBookmark: false, SortKey: lastVisit));
                }
            }

            return results;
        });

        var bookmarks = combined.Where(e => e.IsBookmark).ToList();
        var history = combined.Where(e => !e.IsBookmark).ToList();
        return (bookmarks, history);
    }
}
