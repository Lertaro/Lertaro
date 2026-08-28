namespace Lertaro.Plugins.ContentSearch.Indexing;

/// <summary>
/// Runtime configuration snapshot for the content indexing engine.
/// </summary>
public sealed class ContentIndexConfig
{
    public IReadOnlyList<string> MonitoredFolders { get; init; } = Array.Empty<string>();
    public long MaxFileSizeBytes { get; init; } = 10 * 1024 * 1024;
    public HashSet<string> AllowedExtensions { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public string FilterPattern => AllowedExtensions.Count > 0
        ? string.Join(";", AllowedExtensions.Select(e => "*" + (e.StartsWith('.') ? e : "." + e)))
        : string.Empty;
}
