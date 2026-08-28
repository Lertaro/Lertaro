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
        var tempDoc = Path.Combine(Path.GetTempPath(), $"test_doc_{Guid.NewGuid():N}.md");

        try
        {
            var content1 = "Architecture overview for content search in Lertaro application.";
            var content2 = "Using SQLite FTS5 for fast full-text querying and snippets.";
            File.WriteAllText(tempDoc, content1 + " " + content2);

            using var db = new ContentSearchDatabase(tempDb);
            db.Initialize();

            var chunks = new List<TextChunk>
            {
                new(0, 0, content1.Length, content1),
                new(1, content1.Length + 1, content2.Length, content2)
            };

            db.InsertOrUpdateFile(tempDoc, DateTime.UtcNow, 1024, chunks);

            var (files, totalChunks) = db.GetStats();
            Assert.AreEqual(1, files);
            Assert.AreEqual(2, totalChunks);

            var hits = db.SearchFts("SQLite FTS5", 10);
            Assert.HasCount(1, hits);
            Assert.AreEqual(tempDoc, hits[0].FilePath);
            Assert.AreEqual(Path.GetFileName(tempDoc), hits[0].FileName);
            Assert.IsTrue(hits[0].Snippet.Contains("FTS5", StringComparison.OrdinalIgnoreCase));

            // Delete file and verify cleanup
            db.DeleteFile(tempDoc);
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
            if (File.Exists(tempDoc))
            {
                try { File.Delete(tempDoc); } catch { }
            }
        }
    }

    [TestMethod]
    public void Database_CjkSubstringAndFuzzySearch_MatchesSuccessfully()
    {
        var tempDb = Path.Combine(Path.GetTempPath(), $"test_db_cjk_{Guid.NewGuid():N}.db");
        var tempDoc = Path.Combine(Path.GetTempPath(), $"test_doc_cjk_{Guid.NewGuid():N}.txt");

        try
        {
            var text1 = "喜羊羊与灰太狼：别看我只是一只羊，绿草因为我变得更香。";
            var text2 = "你好，世界！这是一个关于全文本地语义检索与大模型微调的技术文档。";
            File.WriteAllText(tempDoc, text1 + " " + text2);

            using var db = new ContentSearchDatabase(tempDb);
            db.Initialize();

            var chunks = new List<TextChunk>
            {
                new(0, 0, text1.Length, text1),
                new(1, text1.Length + 1, text2.Length, text2)
            };

            db.InsertOrUpdateFile(tempDoc, DateTime.UtcNow, 1024, chunks);

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
            if (File.Exists(tempDoc))
            {
                try { File.Delete(tempDoc); } catch { }
            }
        }
    }
}
