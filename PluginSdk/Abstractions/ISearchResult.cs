namespace Lertaro.PluginSdk.Abstractions;

/// <summary>
/// Read-only search result data structure exposed to plugins.
/// </summary>
public interface ISearchResult
{
    /// <summary>Name of the file or folder.</summary>
    string Name { get; }

    /// <summary>Full absolute path of the file or folder.</summary>
    string FullPath { get; }

    /// <summary>Directory context where the result action is invoked.</summary>
    string ContextDirectory { get; }

    /// <summary>True if this is a directory, false if a file.</summary>
    bool IsDir { get; }

    /// <summary>True if this search result represents an application.</summary>
    bool IsApplication { get; }

    /// <summary>
    /// Returns a custom highlight mask if supported.
    /// </summary>
    bool[]? GetHighlightMask(string text, string query) => null;

    /// <summary>
    /// Size/Created/Modified/Accessed, already known from the index for most file-index-backed
    /// results (no disk I/O or IPC needed) -- <c>default</c> for results this doesn't apply to (e.g.
    /// plugin-provided, non-file results). See <see cref="FileMetadata"/> for how to tell a
    /// genuinely-unknown value apart from a legitimate zero/default one.
    /// </summary>
    FileMetadata Metadata => default;

    /// <summary>
    /// The host-side instant result's <c>ActionArgument</c>, exactly as the instant provider emitted it
    /// (e.g. <c>activatewindow:12345</c>, <c>kill:4321</c>, <c>cc_exec:{...}</c>). Null for every other
    /// kind of result -- ordinary file/folder rows, plugin search actions, history entries -- whose
    /// action is driven by <see cref="FullPath"/> and friends instead. Providers that act on what an
    /// instant result points at (rather than on a path) read this to identify that target.
    /// </summary>
    string? InstantActionArgument => null;
}
