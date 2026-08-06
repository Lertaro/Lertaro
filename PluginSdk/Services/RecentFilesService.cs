namespace Lertaro.PluginSdk.Services;

/// <summary>The most recently changed files across a set of directories, newest first.</summary>
/// <remarks>
/// The host's own recency query, which walks its in-memory index rather than the disk: asking for the
/// recent files under a large tree costs nothing like enumerating that tree and sorting it, which is
/// what a plugin would otherwise have to do with
/// <see cref="DirectoryIndexerService.EnumerateDirectoryAsync"/>.
///
/// Across the directories as one list, not one list per directory: what comes back is the newest
/// entries of the whole set, merged by their real modified times, so a plugin can offer several watched
/// folders as a single "what did I just touch" list.
/// </remarks>
public static class RecentFilesService
{
    /// <summary>Set by the host at startup.</summary>
    public static Func<IReadOnlyList<string>, int, int, CancellationToken, Task<IReadOnlyList<Abstractions.ISearchResult>>>? GetRecentFilesFunc { get; set; }

    /// <summary>
    /// The newest entries under <paramref name="directories"/>, most recent first. Files only:
    /// a folder's own modified time changes whenever anything is added to or removed from it, which
    /// would put the folders being worked in at the top of a list meant to show what was worked on.
    /// </summary>
    /// <param name="limit">How many to return at most. 0 for no cap, bounded by the age cutoff alone.</param>
    /// <param name="maxAgeMinutes">
    /// Only entries changed within this many minutes qualify, on top of the cap. Without one, an idle
    /// folder keeps offering month-old files simply because nothing newer exists. 0 for no age limit.
    /// </param>
    /// <remarks>
    /// A directory the host does not index (an unconfigured network share, a drive with indexing off)
    /// contributes nothing rather than being walked live: this is the fast answer or none, and a plugin
    /// that needs the slow one can enumerate the directory itself.
    /// </remarks>
    public static Task<IReadOnlyList<Abstractions.ISearchResult>> GetRecentFilesAsync(
        IReadOnlyList<string> directories, int limit, int maxAgeMinutes, CancellationToken cancellationToken = default)
        => GetRecentFilesFunc?.Invoke(directories, limit, maxAgeMinutes, cancellationToken)
           ?? Task.FromResult<IReadOnlyList<Abstractions.ISearchResult>>(Array.Empty<Abstractions.ISearchResult>());
}
