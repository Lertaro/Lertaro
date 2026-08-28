namespace Lertaro.Plugins.ContentSearch.Indexing;

/// <summary>
/// Represents a discrete text chunk extracted from a source document.
/// </summary>
public readonly record struct TextChunk(
    int ChunkIndex,
    int Offset,
    int Length,
    string Text
);
