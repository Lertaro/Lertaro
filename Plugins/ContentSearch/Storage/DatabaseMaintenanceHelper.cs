using Microsoft.Data.Sqlite;

namespace Lertaro.Plugins.ContentSearch.Storage;

/// <summary>
/// Database space management: footprint reporting and free-page reclamation.
/// Split out purely to keep ContentSearchDatabase under the repository's per-file
/// line limit; these helpers hold no state and always operate on the one connection
/// passed in per call.
/// </summary>
public static class DatabaseMaintenanceHelper
{
    /// <summary>
    /// Runs VACUUM when a large share of the database is free pages left over from
    /// deleted rows, reclaiming the file space. Cheap no-op on a compact database.
    /// </summary>
    public static void VacuumIfBloat(SqliteConnection conn, double maxFreeRatio = 0.3)
    {
        var (pageCount, freePages) = GetPageCounts(conn);
        if (pageCount == 0 || freePages < pageCount * maxFreeRatio)
            return;

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "VACUUM; PRAGMA wal_checkpoint(TRUNCATE);";
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Total on-disk footprint of the index (all database pages, free ones included).
    /// </summary>
    public static long GetDatabasePageBytes(SqliteConnection conn)
    {
        var (pageCount, _) = GetPageCounts(conn);
        using var sizeCmd = conn.CreateCommand();
        sizeCmd.CommandText = "PRAGMA page_size;";
        var pageSize = Convert.ToInt64(sizeCmd.ExecuteScalar() ?? 0L);
        return pageCount * pageSize;
    }

    private static (long PageCount, long FreePages) GetPageCounts(SqliteConnection conn)
    {
        using var countCmd = conn.CreateCommand();
        countCmd.CommandText = "PRAGMA page_count;";
        var pageCount = Convert.ToInt64(countCmd.ExecuteScalar() ?? 0L);

        using var freeCmd = conn.CreateCommand();
        freeCmd.CommandText = "PRAGMA freelist_count;";
        var freePages = Convert.ToInt64(freeCmd.ExecuteScalar() ?? 0L);

        return (pageCount, freePages);
    }
}
