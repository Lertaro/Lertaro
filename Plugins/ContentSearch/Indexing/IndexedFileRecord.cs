namespace Lertaro.Plugins.ContentSearch.Indexing;

/// <summary>
/// Persisted metadata of an indexed file stored in the local SQLite database.
/// </summary>
public sealed class IndexedFileRecord
{
    public long Id { get; set; }
    public string Path { get; set; } = string.Empty;
    public long LastModified { get; set; }
    public long FileSize { get; set; }
    public long IndexedAt { get; set; }

    /// <summary>
    /// Unix timestamp of the last failed extraction attempt, or null when the file
    /// was successfully indexed. Failed rows carry no FTS content but keep their
    /// mtime/size so unchanged files are not re-extracted on every scan.
    /// </summary>
    public long? FailedAt { get; set; }

    /// <summary>
    /// Hex XxHash-128 of the file bytes for dedup-eligible (large) files, else null.
    /// </summary>
    public string? ContentHash { get; set; }

    /// <summary>
    /// Id of the source row whose text this duplicate reuses, or null when the row holds
    /// its own text (or is a failed row).
    /// </summary>
    public long? ContentRef { get; set; }
}
