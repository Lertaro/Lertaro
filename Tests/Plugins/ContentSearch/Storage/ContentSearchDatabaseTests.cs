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
}
