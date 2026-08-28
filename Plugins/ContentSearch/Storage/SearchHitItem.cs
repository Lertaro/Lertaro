namespace Lertaro.Plugins.ContentSearch.Storage;

/// <summary>
/// Represents an individual content search hit returned by the search engine.
/// </summary>
public sealed class SearchHitItem
{
    public required string FilePath { get; init; }
    public required string FileName { get; init; }
    public required string DirectoryPath { get; init; }
    public required int ChunkIndex { get; init; }
    public required string Snippet { get; init; }
    public required double Score { get; init; }
}
