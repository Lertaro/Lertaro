using Microsoft.Data.Sqlite;

namespace Lertaro.Plugins.BrowserData.Readers;

// Chrome/Edge/Brave-family "History" SQLite file: `urls` table has one row per distinct URL, with
// `last_visit_time` (a monotonically increasing timestamp in Chrome's own epoch/units -- never
// converted here, only ever compared to other Chromium entries as a recency sort key).
internal static class ChromiumHistoryReader
{
    // Bounded so a heavy-history profile can't make every keystroke's in-memory scan (BrowserDataInstantProvider)
    // unbounded -- most-recent visits are what's actually useful to search for anyway.
    private const int MaxEntries = 2000;

    public static List<BrowserEntry> Read(string profileDir)
    {
        var sourcePath = Path.Combine(profileDir, "History");
        if (!File.Exists(sourcePath))
            return new List<BrowserEntry>();

        return SqliteCopyReader.ReadCopy(sourcePath, tempPath =>
        {
            var results = new List<BrowserEntry>();
            // Pooling=false: Microsoft.Data.Sqlite's default connection pool keeps the native file
            // handle open after Dispose() in case the same connection string gets reused -- it never
            // does here (tempPath is a fresh GUID every call), so pooling only left the temp file
            // locked open by this very process, making SqliteCopyReader's own delete-after-use fail.
            using var conn = new SqliteConnection($"Data Source={tempPath};Mode=ReadOnly;Pooling=false");
            conn.Open();
            using var cmd = conn.CreateCommand();
            // Filtered in SQL (not just after reading) so the LIMIT budget -- the most-recent MaxEntries
            // rows -- isn't spent on chrome-extension://, chrome://, file://, ... entries that would
            // just get discarded anyway, potentially crowding out real recent http(s) history.
            cmd.CommandText = "SELECT url, title, last_visit_time FROM urls WHERE hidden = 0 AND url LIKE 'http%' ORDER BY last_visit_time DESC LIMIT $limit";
            cmd.Parameters.AddWithValue("$limit", MaxEntries);
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
                results.Add(new BrowserEntry(
                    string.IsNullOrWhiteSpace(title) ? url : title,
                    url,
                    IsBookmark: false,
                    SortKey: lastVisit,
                    VisitTime: BrowserHistoryTime.FromChromium(lastVisit)));
            }
            return results;
        });
    }
}
