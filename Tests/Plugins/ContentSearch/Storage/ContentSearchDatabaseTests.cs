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
    public void CountIndexedFiles_ExcludesFailedRows()
    {
        var tempDb = Path.Combine(Path.GetTempPath(), $"test_db_count_{Guid.NewGuid():N}.db");
        try
        {
            using var db = new ContentSearchDatabase(tempDb);
            db.Initialize();

            var now = DateTime.UtcNow;
            db.InsertOrUpdateBatch(new[]
            {
                new FileIndexBatchItem(@"C:\docs\good.txt", now, 10, "indexed text"),
                new FileIndexBatchItem(@"C:\docsad.pdf", now, 20, string.Empty)
            });

            Assert.AreEqual(1, db.CountIndexedFiles());
            Assert.AreEqual(2, db.GetStats().TotalFiles);
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
            Assert.IsGreaterThan(0, sizeBefore, "database must report its footprint");

            db.VacuumIfBloat();

            var sizeAfter = db.GetDatabasePageBytes();
            Assert.IsLessThanOrEqualTo(sizeBefore, sizeAfter, $"vacuum must not grow the database ({sizeBefore} -> {sizeAfter})");

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

    private static T WithOneDocument<T>(string text, Func<ContentSearchDatabase, T> ask)
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"test_db_window_{Guid.NewGuid():N}.db");
        var docPath = Path.Combine(Path.GetTempPath(), $"test_doc_window_{Guid.NewGuid():N}.txt");
        try
        {
            File.WriteAllText(docPath, text);
            using var db = new ContentSearchDatabase(dbPath);
            db.Initialize();
            db.InsertOrUpdateFile(docPath, DateTime.UtcNow, text.Length, text);
            return ask(db);
        }
        finally
        {
            if (File.Exists(dbPath))
            {
                try { File.Delete(dbPath); } catch { }
            }

            if (File.Exists(docPath))
            {
                try { File.Delete(docPath); } catch { }
            }
        }
    }

    // A term of one or two characters cannot be answered by the trigram index, so the short-token scan
    // reads every indexed document. Reading them is unavoidable; SHIPPING them was not -- asking for 2000
    // rows used to hand the caller the whole text of each match (measured at 35 MB over 496 documents) to
    // render about 120 characters of excerpt. These pin both halves of the change: the excerpt still
    // reaches a match buried deep in a document, and it stays a excerpt.
    [TestMethod]
    public void SearchFts_ShortTokenMatchedDeepInALargeDocument_ExcerptStillCentersOnIt()
    {
        var filler = new string('蓄', 60_000);
        var text = filler + "他的成绩表已公布" + filler;

        var hits = WithOneDocument(text, db => db.SearchFts("他的", 10));

        Assert.HasCount(1, hits);
        Assert.Contains("他的", hits[0].Snippet, $"the excerpt lost a match {filler.Length} characters in: {hits[0].Snippet}");
        Assert.IsLessThanOrEqualTo(300, hits[0].Snippet.Length,
            "the excerpt must stay bounded however long the document is; that bound is the whole point");
    }

    [TestMethod]
    public void SearchFts_ShortTokenMatchedOnlyCaseInsensitively_StillReturnsTheFile()
    {
        // The WHERE folds ASCII case the way LIKE always has, but the window is centered with instr(),
        // which does not. A term that matched only case-insensitively therefore has no position to center
        // on and shows the document's opening instead. Losing the ROW would be the real regression, so
        // that is what this pins.
        var filler = new string('蓄', 4_000);
        var text = filler + "NetWORK adapter note";

        var hits = WithOneDocument(text, db => db.SearchFts("wo", 10));

        Assert.HasCount(1, hits);
        Assert.IsGreaterThan(0, hits[0].Snippet.Length);
    }

    private static List<string> Shape(IEnumerable<SearchHitItem> hits) =>
        hits.Select(h => $"{h.FilePath}|{h.Snippet}|{h.Score}").ToList();

    [TestMethod]
    public void SearchFtsStreamed_AnswersTheSameHitsInTheSameOrderAsSearchFts()
    {
        // One database asked both ways: the streamed answer has to be the collected one row for row, or
        // streaming would be quietly reordering or rewording the hits while it made them arrive sooner.
        var text = "喜羊羊与灰太狼：这是一个关于全文本地语义检索与在线云南支付结算的技术文档。NetworkAdapter 3cudjz.";

        var both = WithOneDocument(text, db => (
            collected: db.SearchFts("云南", 10).ToList(),
            streamed: db.SearchFtsStreamed("云南", 10).ToList()));

        CollectionAssert.AreEqual(Shape(both.collected), Shape(both.streamed));
    }

    [TestMethod]
    public void SearchFtsStreamed_AbandonedHalfway_LeavesTheIndexUsable()
    {
        // The host drops an in-flight stream the moment a newer query arrives. The enumerator's cleanup is
        // what releases the connection it holds, so an early drop must not strand the database.
        var text = "喜羊羊与灰太狼：这是一个关于全文本地语义检索与在线云南支付结算的技术文档。";

        WithOneDocument(text, db =>
        {
            var walk = db.SearchFtsStreamed("云南", 10).GetEnumerator();
            Assert.IsTrue(walk.MoveNext(), "the walk must produce its first hit before it can be dropped");
            walk.Dispose();

            Assert.HasCount(1, db.SearchFts("云南", 10), "and the index still answers after it was dropped");
            return true;
        });
    }
}
