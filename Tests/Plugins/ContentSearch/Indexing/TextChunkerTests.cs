using Lertaro.Plugins.ContentSearch.Indexing;

namespace Lertaro.Plugins.ContentSearch.Tests.Indexing;

[TestClass]
public sealed class TextChunkerTests
{
    [TestMethod]
    public void ChunkText_NullOrEmpty_ReturnsEmptyList()
    {
        var nullResult = TextChunker.ChunkText(null);
        var emptyResult = TextChunker.ChunkText("");
        var whitespaceResult = TextChunker.ChunkText("   \t\n ");

        Assert.IsEmpty(nullResult);
        Assert.IsEmpty(emptyResult);
        Assert.IsEmpty(whitespaceResult);
    }

    [TestMethod]
    public void ChunkText_ShortText_ReturnsSingleChunk()
    {
        var text = "Hello world! This is a short test document.";
        var chunks = TextChunker.ChunkText(text, chunkSize: 350, overlap: 50);

        Assert.HasCount(1, chunks);
        Assert.AreEqual(0, chunks[0].ChunkIndex);
        Assert.AreEqual(text, chunks[0].Text);
    }

    [TestMethod]
    public void ChunkText_LongText_SplitsIntoMultipleChunksWithOverlap()
    {
        var sentence = "The quick brown fox jumps over the lazy dog. ";
        var repeatedText = string.Concat(Enumerable.Repeat(sentence, 20)); // ~900 chars

        var chunks = TextChunker.ChunkText(repeatedText, chunkSize: 200, overlap: 40);

        Assert.IsGreaterThan(1, chunks.Count);
        for (var i = 0; i < chunks.Count; i++)
        {
            Assert.AreEqual(i, chunks[i].ChunkIndex);
            Assert.IsNotEmpty(chunks[i].Text);
            Assert.IsLessThanOrEqualTo(250, chunks[i].Text.Length);
        }
    }

    [TestMethod]
    public void ChunkText_PunctuationBoundary_SplitsAtSentenceBoundary()
    {
        var part1 = new string('A', 180) + "。";
        var part2 = new string('B', 180) + "。";
        var fullText = part1 + part2;

        var chunks = TextChunker.ChunkText(fullText, chunkSize: 200, overlap: 30);

        Assert.IsGreaterThanOrEqualTo(2, chunks.Count);
        Assert.EndsWith("。", chunks[0].Text);
    }
}
