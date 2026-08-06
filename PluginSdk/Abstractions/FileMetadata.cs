namespace Lertaro.PluginSdk.Abstractions;

/// <summary>
/// A file's Size and Created/Modified/Accessed timestamps (local time). <c>default</c> (every field
/// zero/<see cref="DateTime.MinValue"/>) means "not available" -- e.g. a search result that isn't
/// backed by the file index (see <see cref="ISearchResult.Metadata"/>). Check <see cref="Modified"/>
/// against <see cref="DateTime.MinValue"/> to tell a genuinely-unknown result apart from a real,
/// zero-byte file (whose <see cref="Size"/> is legitimately 0 but whose timestamps are still real).
/// </summary>
public readonly record struct FileMetadata(long Size, DateTime Created, DateTime Modified, DateTime Accessed);
