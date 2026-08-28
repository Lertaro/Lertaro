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
}
