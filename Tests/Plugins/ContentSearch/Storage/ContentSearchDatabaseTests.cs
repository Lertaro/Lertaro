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
            var fullContent = content1 + " " + content2;
            File.WriteAllText(tempDoc, fullContent);

            using var db = new ContentSearchDatabase(tempDb);
            db.Initialize();

            db.InsertOrUpdateFile(tempDoc, DateTime.UtcNow, 1024, fullContent);

            var (files, _) = db.GetStats();
            Assert.AreEqual(1, files);

            var hits = db.SearchFts("SQLite FTS5", 10);
            Assert.HasCount(1, hits);
            Assert.AreEqual(tempDoc, hits[0].FilePath);
            Assert.AreEqual(Path.GetFileName(tempDoc), hits[0].FileName);
            Assert.Contains("FTS5", hits[0].Snippet, StringComparison.OrdinalIgnoreCase);

            // Delete file and verify cleanup
            db.DeleteFile(tempDoc);
            var (afterFiles, _) = db.GetStats();
            Assert.AreEqual(0, afterFiles);

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
    public void Database_CjkAndShortQueries_MatchesSuccessfully()
    {
        var tempDb = Path.Combine(Path.GetTempPath(), $"test_db_cjk_{Guid.NewGuid():N}.db");
        var tempDoc = Path.Combine(Path.GetTempPath(), $"test_doc_cjk_{Guid.NewGuid():N}.txt");

        try
        {
            var text1 = "喜羊羊与灰太狼：别看我只是一只羊，绿草因为我变得更香。";
            var text2 = "你好世界！这是一个关于全文本地语义检索与在线云南支付结算的技术文档。NetworkAdapter 3cudjz.";
            var fullText = text1 + " " + text2;
            File.WriteAllText(tempDoc, fullText);

            using var db = new ContentSearchDatabase(tempDb);
            db.Initialize();

            db.InsertOrUpdateFile(tempDoc, DateTime.UtcNow, 1024, fullText);

            // 1-character CJK search
            var hits1Char = db.SearchFts("羊", 10);
            Assert.HasCount(1, hits1Char);
            Assert.Contains("羊", hits1Char[0].Snippet);

            // 2-character CJK search
            var hits2Char = db.SearchFts("云南", 10);
            Assert.HasCount(1, hits2Char);
            Assert.Contains("云南", hits2Char[0].Snippet);

            // 3-character CJK search
            var hits3Char = db.SearchFts("只是一只羊", 10);
            Assert.HasCount(1, hits3Char);
            Assert.Contains("只是一只羊", hits3Char[0].Snippet);

            // English 2-character search
            var hitsEn2Char = db.SearchFts("jz", 10);
            Assert.HasCount(1, hitsEn2Char);
            Assert.Contains("jz", hitsEn2Char[0].Snippet, StringComparison.OrdinalIgnoreCase);

            // English arbitrary substring search
            var hitsEnSubstring = db.SearchFts("work", 10);
            Assert.HasCount(1, hitsEnSubstring);
            Assert.Contains("Network", hitsEnSubstring[0].Snippet, StringComparison.OrdinalIgnoreCase);
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
    public void VacuumIfBloat_ReclaimsFreePagesAfterMassDeletion()
    {
        var tempDb = Path.Combine(Path.GetTempPath(), $"test_db_vacuum_{Guid.NewGuid():N}.db");

        try
        {
            using var db = new ContentSearchDatabase(tempDb);
            db.Initialize();

            var bulkContent = new string('v', 64 * 1024) + " vacuummarker";
            for (var i = 0; i < 40; i++)
            {
                db.InsertOrUpdateFile($@"C:\bulk\file{i}.txt", DateTime.UtcNow, bulkContent.Length, bulkContent);
            }

            // Deleting most rows leaves free pages; the vacuum reclaims them.
            for (var i = 0; i < 38; i++)
            {
                db.DeleteFile($@"C:\bulk\file{i}.txt");
            }

            var sizeBefore = db.GetDatabasePageBytes();
            Assert.IsTrue(sizeBefore > 0, "database must report its footprint");

            db.VacuumIfBloat();

            var sizeAfter = db.GetDatabasePageBytes();
            Assert.IsTrue(sizeAfter <= sizeBefore, $"vacuum must not grow the database ({sizeBefore} -> {sizeAfter})");

            // The two surviving rows stay searchable after the vacuum.
            Assert.HasCount(2, db.SearchFts("vacuummarker", 10));
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
