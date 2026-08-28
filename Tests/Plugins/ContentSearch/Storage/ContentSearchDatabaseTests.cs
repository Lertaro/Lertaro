using Lertaro.Plugins.ContentSearch.Indexing;
using Lertaro.Plugins.ContentSearch.Storage;

namespace Lertaro.Plugins.ContentSearch.Tests.Storage;

[TestClass]
public sealed class ContentSearchDatabaseTests
{
    [TestMethod]
    public void Database_InsertAndSearch_ReturnsMatchingHit()
    {
        var tempDb = Path.Combine(Path.GetTempPath(), $"test_db_{Guid.NewGuid():N}.db");

        try
        {
            using var db = new ContentSearchDatabase(tempDb);
            db.Initialize();

            var chunks = new List<TextChunk>
            {
                new(0, 0, 50, "Architecture overview for content search in Lertaro application."),
                new(1, 40, 50, "Using SQLite FTS5 for fast full-text querying and snippets.")
            };

            db.InsertOrUpdateFile(@"C:\Docs\Architecture.md", DateTime.UtcNow, 1024, chunks);

            var (files, totalChunks) = db.GetStats();
            Assert.AreEqual(1, files);
            Assert.AreEqual(2, totalChunks);

            var hits = db.SearchFts("SQLite FTS5", 10);
            Assert.HasCount(1, hits);
            Assert.AreEqual(@"C:\Docs\Architecture.md", hits[0].FilePath);
            Assert.AreEqual("Architecture.md", hits[0].FileName);
            Assert.IsTrue(hits[0].Snippet.Contains("FTS5", StringComparison.OrdinalIgnoreCase));

            // Delete file and verify cleanup
            db.DeleteFile(@"C:\Docs\Architecture.md");
            var (afterFiles, afterChunks) = db.GetStats();
            Assert.AreEqual(0, afterFiles);
            Assert.AreEqual(0, afterChunks);

            var afterHits = db.SearchFts("SQLite", 10);
            Assert.IsEmpty(afterHits);
        }
        finally
        {
            if (File.Exists(tempDb))
            {
                try { File.Delete(tempDb); } catch { }
            }
        }
    }

    [TestMethod]
    public void Database_CjkSubstringAndFuzzySearch_MatchesSuccessfully()
    {
        var tempDb = Path.Combine(Path.GetTempPath(), $"test_db_cjk_{Guid.NewGuid():N}.db");

        try
        {
            using var db = new ContentSearchDatabase(tempDb);
            db.Initialize();

            var chunks = new List<TextChunk>
            {
                new(0, 0, 50, "喜羊羊与灰太狼：别看我只是一只羊，绿草因为我变得更香。"),
                new(1, 40, 50, "你好，世界！这是一个关于全文本地语义检索与大模型微调的技术文档。")
            };

            db.InsertOrUpdateFile(@"C:\Docs\Sample.txt", DateTime.UtcNow, 1024, chunks);

            // Substring search in CJK
            var hits1 = db.SearchFts("只是一只羊", 10);
            Assert.HasCount(1, hits1);
            Assert.Contains("只是一只羊", hits1[0].Snippet);

            var hits2 = db.SearchFts("语义检索", 10);
            Assert.HasCount(1, hits2);
            Assert.Contains("语义检索", hits2[0].Snippet);

            // Multi-token CJK search with spaces (fuzzy terms)
            var hits3 = db.SearchFts("全文本地 语义检索", 10);
            Assert.HasCount(1, hits3);

            var hitsMultiToken = db.SearchFts("你 好", 10);
            Assert.HasCount(1, hitsMultiToken);

            var hitsFuzzyChars = db.SearchFts("喜 羊", 10);
            Assert.HasCount(1, hitsFuzzyChars);

            // Single character search
            var hits4 = db.SearchFts("羊", 10);
            Assert.HasCount(1, hits4);
        }
        finally
        {
            if (File.Exists(tempDb))
            {
                try { File.Delete(tempDb); } catch { }
            }
        }
    }
}
