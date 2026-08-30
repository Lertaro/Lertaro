using System.Diagnostics;
using Lertaro.Plugins.ContentSearch.Storage;
using Microsoft.Data.Sqlite;

namespace Lertaro.Plugins.ContentSearch.Tests.Storage;

// One-shot comparison harness for the Lucene experiment branch: runs the SAME corpus and the
// SAME query set against a bare SQLite FTS5 trigram table and a bare LuceneContentIndex, then
// prints index time, index size, query times and delete time for both. Kept on the branch so
// the numbers can be re-produced; not part of the regular regression suite. Both engines run
// bare (no hit-expansion joins), so the numbers isolate the engines themselves.
[TestClass]
public sealed class LuceneFtsBenchmark
{
    private const int DocCount = 300;

    private static readonly string[] EnglishWords =
    [
        "content", "search", "engine", "index", "token", "query", "text", "document",
        "lucene", "sqlite", "trigram", "phrase", "match", "score", "ranking", "storage"
    ];

    private static readonly string[] ChineseWords = ["搜索", "引擎", "内容", "索引", "分词", "查询", "存储", "排序"];

    private static string MakeDoc(int i)
    {
        var sb = new System.Text.StringBuilder(48 * 1024);
        var random = new Random(1234 + i);
        sb.Append("Uniquemarker").Append(i).AppendLine();
        for (var line = 0; line < 400; line++)
        {
            for (var word = 0; word < 12; word++)
            {
                sb.Append(EnglishWords[random.Next(EnglishWords.Length)]).Append(' ');
            }
            for (var word = 0; word < 6; word++)
            {
                sb.Append(ChineseWords[random.Next(ChineseWords.Length)]);
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }

    [TestMethod]
    public void Compare_Backends()
    {
        Console.WriteLine($"Corpus: {DocCount} docs x ~45KB (~13MB text)");

        var corpus = Enumerable.Range(0, DocCount).Select(MakeDoc).ToList();

        // --- SQLite FTS5 trigram (the production schema before the experiment) ---
        var ftsDir = Path.Combine(Path.GetTempPath(), "LuceneBench_Fts_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(ftsDir);
        var sw = Stopwatch.StartNew();
        using (var conn = OpenFtsSchema(Path.Combine(ftsDir, "bench.db")))
        {
            InsertFts(conn, corpus);
            sw.Stop();
            Console.WriteLine($"INDEX  : FTS5 {sw.ElapsedMilliseconds} ms / {DirBytes(ftsDir) / 1024} KB  |  Lucene (below)");

            TimedQuery(conn, "unique marker     ", () => QueryFts(conn, "\"Uniquemarker" + DocCount / 2 + "\""));
            TimedQuery(conn, "3-word phrase     ", () => QueryFts(conn, "\"content search engine\""));
            TimedQuery(conn, "2-char CJK (LIKE) ", () => QueryFtsLike(conn, "搜索"));
            TimedQuery(conn, "5-char CJK phrase ", () => QueryFts(conn, "\"内容搜索引擎\""));

            sw.Restart();
            using (var tx = conn.BeginTransaction())
            {
                for (var i = 0; i < DocCount / 2; i++)
                {
                    using var cmd = conn.CreateCommand();
                    cmd.Transaction = tx;
                    cmd.CommandText = "DELETE FROM files WHERE path = @p; DELETE FROM files_fts WHERE rowid IN (SELECT id FROM files WHERE path = @p);";
                    cmd.Parameters.AddWithValue("@p", PathFor(i));
                    cmd.ExecuteNonQuery();
                }
                tx.Commit();
            }
            sw.Stop();
            Console.WriteLine($"DELETE half: FTS5 {sw.ElapsedMilliseconds} ms");
        }

        // --- Lucene n-gram ---
        var luceneDir = Path.Combine(Path.GetTempPath(), "LuceneBench_Luc_" + Guid.NewGuid().ToString("N"));
        var lucene = new LuceneContentIndex(luceneDir);
        sw.Restart();
        for (var i = 0; i < DocCount; i++)
        {
            lucene.Upsert(PathFor(i), corpus[i]);
        }
        lucene.Commit();
        sw.Stop();
        Console.WriteLine($"INDEX  : Lucene {sw.ElapsedMilliseconds} ms / {lucene.GetBytes() / 1024} KB");

        TimedLucene(lucene, "unique marker     ", () => lucene.Search("Uniquemarker" + DocCount / 2, 30).Count);
        TimedLucene(lucene, "3-word phrase     ", () => lucene.Search("content search engine", 30).Count);
        TimedLucene(lucene, "2-char CJK (LIKE) ", () => lucene.Search("搜索", 30).Count);
        TimedLucene(lucene, "5-char CJK phrase ", () => lucene.Search("内容搜索引擎", 30).Count);

        sw.Restart();
        var paths = new List<string>();
        for (var i = 0; i < DocCount / 2; i++) paths.Add(PathFor(i));
        lucene.DeletePaths(paths);
        lucene.Commit();
        sw.Stop();
        Console.WriteLine($"DELETE half: Lucene {sw.ElapsedMilliseconds} ms");

        lucene.Dispose();
        try { Directory.Delete(ftsDir, true); } catch { }
        try { Directory.Delete(luceneDir, true); } catch { }
    }

    private static string PathFor(int i) => $@"C:\bench\doc{i}.txt";

    private static SqliteConnection OpenFtsSchema(string dbPath)
    {
        var conn = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = dbPath, Mode = SqliteOpenMode.ReadWriteCreate }.ToString());
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE files (id INTEGER PRIMARY KEY AUTOINCREMENT, path TEXT UNIQUE NOT NULL);
            CREATE VIRTUAL TABLE files_fts USING fts5(content, tokenize = 'trigram');
            """;
        cmd.ExecuteNonQuery();
        return conn;
    }

    private static void InsertFts(SqliteConnection conn, List<string> corpus)
    {
        using var tx = conn.BeginTransaction();
        for (var i = 0; i < corpus.Count; i++)
        {
            using var insFile = conn.CreateCommand();
            insFile.Transaction = tx;
            insFile.CommandText = "INSERT INTO files (path) VALUES (@p); SELECT last_insert_rowid();";
            insFile.Parameters.AddWithValue("@p", PathFor(i));
            var rowId = (long)(insFile.ExecuteScalar() ?? 0L);

            using var insFts = conn.CreateCommand();
            insFts.Transaction = tx;
            insFts.CommandText = "INSERT INTO files_fts (rowid, content) VALUES (@rowid, @content);";
            insFts.Parameters.AddWithValue("@rowid", rowId);
            insFts.Parameters.AddWithValue("@content", corpus[i]);
            insFts.ExecuteNonQuery();
        }
        tx.Commit();
    }

    private static int QueryFts(SqliteConnection conn, string matchQuery)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT rowid, content FROM files_fts WHERE files_fts MATCH @q LIMIT 30;";
        cmd.Parameters.AddWithValue("@q", matchQuery);
        var count = 0;
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) count++;
        return count;
    }

    private static int QueryFtsLike(SqliteConnection conn, string token)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT rowid, content FROM files_fts WHERE files_fts.content LIKE @t LIMIT 30;";
        cmd.Parameters.AddWithValue("@t", "%" + token + "%");
        var count = 0;
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) count++;
        return count;
    }

    private static void TimedQuery(SqliteConnection conn, string name, Func<int> run)
    {
        var sw = Stopwatch.StartNew();
        var hits = 0;
        for (var i = 0; i < 5; i++) hits = run();
        sw.Stop();
        Console.WriteLine($"QUERY  : {name} FTS5 {sw.ElapsedMilliseconds / 5.0:F2} ms avg ({hits} hits)");
    }

    private static void TimedLucene(LuceneContentIndex lucene, string name, Func<int> run)
    {
        var sw = Stopwatch.StartNew();
        var hits = 0;
        for (var i = 0; i < 5; i++) hits = run();
        sw.Stop();
        Console.WriteLine($"QUERY  : {name} Lucene {sw.ElapsedMilliseconds / 5.0:F2} ms avg ({hits} hits)");
    }

    private static long DirBytes(string dir)
    {
        long total = 0;
        foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            total += new FileInfo(file).Length;
        return total;
    }
}
