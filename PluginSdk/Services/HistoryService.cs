namespace Lertaro.PluginSdk.Services;

/// <summary>
/// What kind of target a <see cref="HistoryEntry"/> points to -- replaces the old "app:"-prefix
/// convention with an explicit field so callers never have to guess/parse it back out.
/// </summary>
public enum HistoryEntryKind { File, Folder, Application }

/// <summary>
/// One recorded history entry: the search keyword that led to it (empty if opened directly, e.g. from
/// a Startup Panel tab with no query typed), the target path/id, its kind, and when it was opened
/// (Unix seconds). A path appears at most once -- if it's opened again under a different keyword, the
/// newer keyword replaces the older one rather than both coexisting.
/// </summary>
public readonly record struct HistoryEntry(string Keyword, string Path, HistoryEntryKind Kind, long Time);

/// <summary>
/// A decoupled service to retrieve search and navigation history from the host application.
/// </summary>
public static class HistoryService
{
    /// <summary>
    /// Delegate function set by the host application to retrieve history entries.
    /// </summary>
    public static Func<IEnumerable<HistoryEntry>>? GetHistoryEntriesFunc { get; set; }

    /// <summary>
    /// Retrieves every recorded history entry (each path appears at most once), most-recently-opened
    /// first.
    /// </summary>
    public static IEnumerable<HistoryEntry> GetHistoryEntries() => GetHistoryEntriesFunc?.Invoke() ?? Array.Empty<HistoryEntry>();
}
