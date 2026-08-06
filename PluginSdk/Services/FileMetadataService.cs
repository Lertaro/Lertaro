using Lertaro.PluginSdk.Abstractions;

namespace Lertaro.PluginSdk.Services;

/// <summary>
/// Lets plugins fetch Size/Created/Modified/Accessed for a batch of paths in one call. The host
/// answers from its in-memory file index where possible (no disk I/O) and falls back to a live
/// filesystem stat only for paths it isn't tracking -- callers never need to stat files themselves.
/// Most search results already carry this via <see cref="ISearchResult.Metadata"/> without needing a
/// call here at all; reach for this service only for a path that ISN'T one of the current results
/// (e.g. one a plugin discovered some other way).
/// </summary>
public static class FileMetadataService
{
    /// <summary>
    /// Delegate function set by the main application to perform the batched lookup.
    /// </summary>
    public static Func<IReadOnlyList<string>, Task<IReadOnlyDictionary<string, FileMetadata>>>? BatchLookupFunc { get; set; }

    /// <summary>
    /// Gets metadata for the given paths. Paths that don't exist are simply absent from the result.
    /// </summary>
    public static Task<IReadOnlyDictionary<string, FileMetadata>> GetMetadataAsync(IReadOnlyList<string> paths) =>
        BatchLookupFunc?.Invoke(paths) ?? Task.FromResult<IReadOnlyDictionary<string, FileMetadata>>(new Dictionary<string, FileMetadata>());
}
