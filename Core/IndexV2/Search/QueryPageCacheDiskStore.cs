using System.Text;

using Lertaro.Core.SearchIndex;

namespace Lertaro.Core.IndexV2.Search;

/// <summary>
/// Persists <see cref="QueryPageCache"/> pages for a drive so the first search of a session can answer from
/// disk instead of scanning the index again.
/// </summary>
/// <remarks>
/// The in-memory cache alone only helps within one service lifetime: the service is restarted by every
/// update, and each restart pays the full scan again for the queries a session starts with. A page is only a
/// valid answer for the snapshot it was scanned from, so the file records that snapshot's identity
/// (source key, USN journal id, next USN) and a load whose identity does not match the index now open is
/// refused outright -- a stale page can never be served.
///
/// Only single-character queries are worth this: they are what a session starts with, they are the broadest
/// and therefore slowest scans, and the whole set is a few megabytes. Longer queries are left to the
/// in-memory cache.
///
/// The format is deliberately private to this file and versioned. It stores only what a search produces, not
/// what the UI derives, and any change to <see cref="SearchResult"/>'s search-relevant fields must bump
/// <see cref="Version"/> so old files are discarded rather than misread.
/// </remarks>
internal static class QueryPageCacheDiskStore
{
    private const int Magic = 0x4350514C; // "LQPC"
    private const byte Version = 1;

    /// <summary>Rows kept per query. Matches the in-memory cap; a prefix is still a valid answer.</summary>
    private const int MaxRowsPerQuery = 512;

    public static void Save(
        string path,
        string sourceKey,
        ulong journalId,
        long nextUsn,
        IEnumerable<KeyValuePair<string, List<SearchResult>>> pages)
    {
        var temp = path + ".tmp";
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var writer = new BinaryWriter(stream, Encoding.UTF8))
        {
            writer.Write(Magic);
            writer.Write(Version);
            writer.Write(sourceKey);
            writer.Write(journalId);
            writer.Write(nextUsn);

            // Count first: a partially written file must not be readable, and the count is the cheapest
            // thing to validate against the entries that follow.
            var entries = pages.Where(pair => pair.Value.Count > 0).ToList();
            writer.Write(entries.Count);
            foreach (var pair in entries)
            {
                writer.Write(pair.Key);
                var rows = pair.Value.Count > MaxRowsPerQuery ? pair.Value.GetRange(0, MaxRowsPerQuery) : pair.Value;
                writer.Write(rows.Count);
                foreach (var row in rows)
                {
                    writer.Write(row.Path);
                    writer.Write(row.Name);
                    writer.Write(row.IsDir);
                    writer.Write(row.Drive);
                    writer.Write((int)row.Attributes);
                    writer.Write(row.RankSortKey);
                }
            }
        }

        // Swap into place so a crash mid-write leaves the previous file intact instead of a torn one.
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>
    /// Reads the pages for <paramref name="sourceKey"/>, or returns null when the file is absent, unreadable,
    /// written by a different version, or describes a different snapshot than the one now open.
    /// </summary>
    public static Dictionary<string, List<SearchResult>>? Load(
        string path,
        string sourceKey,
        ulong journalId,
        long nextUsn)
    {
        if (!File.Exists(path))
            return null;

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var reader = new BinaryReader(stream, Encoding.UTF8);

            if (reader.ReadInt32() != Magic || reader.ReadByte() != Version)
                return null;
            if (!string.Equals(reader.ReadString(), sourceKey, StringComparison.OrdinalIgnoreCase))
                return null;

            // The drive identity is what has to match: a page remembered from a different journal belongs to
            // a different volume and its paths may not exist at all. The USN position is read but NOT
            // compared. It advances on every service start (the journal catch-up replays whatever changed
            // while the service was down), so comparing it made every saved page dead on arrival -- measured
            // as the whole set being rebuilt on each restart, which is exactly what this file exists to avoid.
            // A page older than the index can therefore miss a file created while the service was down; the
            // in-memory revision check still retires it as soon as the index changes in this run, and the
            // next search that misses simply scans.
            if (reader.ReadUInt64() != journalId)
                return null;
            _ = reader.ReadInt64();

            var count = reader.ReadInt32();
            if (count is < 0 or > 4096)
                return null;

            var pages = new Dictionary<string, List<SearchResult>>(count, StringComparer.Ordinal);
            for (var i = 0; i < count; i++)
            {
                var query = reader.ReadString();
                var rowCount = reader.ReadInt32();
                if (rowCount is < 0 or > MaxRowsPerQuery)
                    return null;

                var rows = new List<SearchResult>(rowCount);
                for (var r = 0; r < rowCount; r++)
                {
                    rows.Add(new SearchResult
                    {
                        Path = reader.ReadString(),
                        Name = reader.ReadString(),
                        IsDir = reader.ReadBoolean(),
                        Drive = reader.ReadString(),
                        Attributes = (FileAttributes)reader.ReadInt32(),
                        RankSortKey = reader.ReadUInt64()
                    });
                }

                pages[query] = rows;
            }

            return pages;
        }
        catch (Exception ex)
        {
            // A corrupt or unreadable cache is a slow first search, never a wrong answer: drop it and scan.
            Logger.Log($"[QueryPageCacheDiskStore] Ignoring unreadable cache {path}: {ex.Message}", LogLevel.Debug);
            return null;
        }
    }

    /// <summary>File name for one drive's pages. Drive letters are legal in a file name.</summary>
    public static string PathFor(string directory, string sourceKey)
    {
        var safe = sourceKey.TrimEnd(':', '\\').Replace('/', '_');
        return System.IO.Path.Combine(directory, safe + ".searchcache");
    }
}
