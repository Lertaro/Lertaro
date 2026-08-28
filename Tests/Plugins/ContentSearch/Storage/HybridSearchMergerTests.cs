using Lertaro.Plugins.ContentSearch.Storage;

namespace Lertaro.Plugins.ContentSearch.Tests.Storage;

[TestClass]
public sealed class HybridSearchMergerTests
{
    [TestMethod]
    public void MergeRrf_EmptyInputs_ReturnsEmpty()
    {
        var result = HybridSearchMerger.MergeRrf(Array.Empty<SearchHitItem>(), null);
        Assert.IsEmpty(result);
    }

    [TestMethod]
    public void MergeRrf_LexicalOnly_CalculatesValidRrfScores()
    {
        var lexical = new List<SearchHitItem>
        {
            new() { FilePath = "C:/doc1.txt", FileName = "doc1.txt", DirectoryPath = "C:/", ChunkIndex = 0, Snippet = "match 1", Score = 10.0 },
            new() { FilePath = "C:/doc2.txt", FileName = "doc2.txt", DirectoryPath = "C:/", ChunkIndex = 0, Snippet = "match 2", Score = 5.0 }
        };

        var merged = HybridSearchMerger.MergeRrf(lexical, null, k: 60);

        Assert.HasCount(2, merged);
        Assert.AreEqual("C:/doc1.txt", merged[0].FilePath);
        Assert.AreEqual(1.0 / 61.0, merged[0].Score, 0.0001);
        Assert.AreEqual("C:/doc2.txt", merged[1].FilePath);
        Assert.AreEqual(1.0 / 62.0, merged[1].Score, 0.0001);
    }

    [TestMethod]
    public void MergeRrf_CombinedLexicalAndVector_AggregatesScoresAndSorts()
    {
        var lexical = new List<SearchHitItem>
        {
            new() { FilePath = "C:/docA.txt", FileName = "docA.txt", DirectoryPath = "C:/", ChunkIndex = 0, Snippet = "lexical A", Score = 1.0 },
            new() { FilePath = "C:/docB.txt", FileName = "docB.txt", DirectoryPath = "C:/", ChunkIndex = 0, Snippet = "lexical B", Score = 0.5 }
        };

        var vector = new List<SearchHitItem>
        {
            new() { FilePath = "C:/docB.txt", FileName = "docB.txt", DirectoryPath = "C:/", ChunkIndex = 0, Snippet = "vector B", Score = 0.9 },
            new() { FilePath = "C:/docC.txt", FileName = "docC.txt", DirectoryPath = "C:/", ChunkIndex = 0, Snippet = "vector C", Score = 0.8 }
        };

        var merged = HybridSearchMerger.MergeRrf(lexical, vector, k: 60);

        Assert.HasCount(3, merged);
        Assert.AreEqual("C:/docB.txt", merged[0].FilePath);
        Assert.AreEqual("C:/docA.txt", merged[1].FilePath);
        Assert.AreEqual("C:/docC.txt", merged[2].FilePath);
    }
}
