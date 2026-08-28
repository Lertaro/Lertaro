namespace Lertaro.Plugins.ContentSearch.Indexing;

/// <summary>
/// Splits document text into overlapping windows for indexing and retrieval.
/// </summary>
public static class TextChunker
{
    public const int DefaultChunkSize = 350;
    public const int DefaultOverlap = 50;

    private static readonly char[] BoundaryDelimiters =
    [
        '\n', '\r', '。', '！', '？', '；', '.', '!', '?', ';', ' '
    ];

    public static IReadOnlyList<TextChunk> ChunkText(
        string? text,
        int chunkSize = DefaultChunkSize,
        int overlap = DefaultOverlap)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Array.Empty<TextChunk>();

        if (chunkSize <= 0)
            chunkSize = DefaultChunkSize;

        if (overlap < 0 || overlap >= chunkSize)
            overlap = Math.Min(50, chunkSize / 2);

        var trimmed = text.Trim();
        if (trimmed.Length <= chunkSize)
        {
            return new[] { new TextChunk(0, 0, trimmed.Length, trimmed) };
        }

        var chunks = new List<TextChunk>();
        var currentStart = 0;
        var chunkIndex = 0;
        var textLength = text.Length;

        while (currentStart < textLength)
        {
            // Skip leading whitespace for chunk readability while preserving offset alignment
            while (currentStart < textLength && char.IsWhiteSpace(text[currentStart]))
                currentStart++;

            if (currentStart >= textLength)
                break;

            var remaining = textLength - currentStart;
            if (remaining <= chunkSize)
            {
                var finalChunkText = text.Substring(currentStart, remaining).Trim();
                if (finalChunkText.Length > 0)
                {
                    chunks.Add(new TextChunk(chunkIndex++, currentStart, remaining, finalChunkText));
                }
                break;
            }

            // Search for natural breaking boundary within the overlap buffer
            var searchStart = currentStart + chunkSize - overlap;
            var searchEnd = currentStart + chunkSize;
            var splitIndex = FindBestSplitPoint(text, searchStart, searchEnd);

            var chunkLength = splitIndex - currentStart;
            var chunkText = text.Substring(currentStart, chunkLength).Trim();

            if (chunkText.Length > 0)
            {
                chunks.Add(new TextChunk(chunkIndex++, currentStart, chunkLength, chunkText));
            }

            // Advance start position with overlap
            var nextStart = Math.Max(splitIndex - overlap, currentStart + 1);
            if (nextStart <= currentStart)
            {
                nextStart = currentStart + chunkSize - overlap;
            }

            currentStart = nextStart;
        }

        return chunks;
    }

    private static int FindBestSplitPoint(string text, int searchStart, int searchEnd)
    {
        var clampedStart = Math.Clamp(searchStart, 0, text.Length);
        var clampedEnd = Math.Clamp(searchEnd, clampedStart, text.Length);

        // Search backward from the window end for a natural delimiter
        for (var i = clampedEnd - 1; i >= clampedStart; i--)
        {
            var ch = text[i];
            if (Array.IndexOf(BoundaryDelimiters, ch) >= 0)
            {
                return i + 1; // Include delimiter in the preceding chunk
            }
        }

        return clampedEnd;
    }
}
