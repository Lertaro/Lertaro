using Microsoft.Data.Sqlite;
using Lertaro.Plugins.BrowserData.Readers;

namespace Lertaro.Plugins.BrowserData.Tests.Readers;

[TestClass]
public sealed class ChromiumHistoryReaderTests
{
    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("lertaro-tests-").FullName;

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { }
        }
    }

    private static void CreateHistoryDb(string path, params (string Url, string Title, long LastVisit, int Hidden)[] rows)
    {
        using var conn = new SqliteConnection($"Data Source={path}");
        conn.Open();
        using (var create = conn.CreateCommand())
        {
            create.CommandText = "CREATE TABLE urls (id INTEGER PRIMARY KEY, url TEXT, title TEXT, last_visit_time INTEGER, hidden INTEGER)";
            create.ExecuteNonQuery();
        }
        foreach (var row in rows)
        {
            using var insert = conn.CreateCommand();
            insert.CommandText = "INSERT INTO urls (url, title, last_visit_time, hidden) VALUES ($url, $title, $lv, $hidden)";
            insert.Parameters.AddWithValue("$url", row.Url);
            insert.Parameters.AddWithValue("$title", row.Title);
            insert.Parameters.AddWithValue("$lv", row.LastVisit);
            insert.Parameters.AddWithValue("$hidden", row.Hidden);
            insert.ExecuteNonQuery();
        }
    }

    [TestMethod]
    public void Read_NoHistoryFile_ReturnsEmpty()
    {
        using var dir = new TempDirectory();

        Assert.IsEmpty(ChromiumHistoryReader.Read(dir.Path));
    }

    [TestMethod]
    public void Read_ValidHistoryDb_ReturnsEntriesOrderedByMostRecentFirst()
    {
        using var dir = new TempDirectory();
        CreateHistoryDb(Path.Combine(dir.Path, "History"),
            ("https://old.com", "Old", 100, 0),
            ("https://new.com", "New", 200, 0));

        var entries = ChromiumHistoryReader.Read(dir.Path);

        Assert.HasCount(2, entries);
        Assert.AreEqual("New", entries[0].Title);
        Assert.AreEqual("Old", entries[1].Title);
        Assert.IsTrue(entries.All(e => !e.IsBookmark));
    }

    [TestMethod]
    public void Read_HiddenEntry_IsExcluded()
    {
        using var dir = new TempDirectory();
        CreateHistoryDb(Path.Combine(dir.Path, "History"), ("https://hidden.com", "Hidden", 100, 1));

        Assert.IsEmpty(ChromiumHistoryReader.Read(dir.Path));
    }

    [TestMethod]
    public void Read_NonHttpEntry_IsExcluded()
    {
        using var dir = new TempDirectory();
        CreateHistoryDb(Path.Combine(dir.Path, "History"), ("chrome://settings", "Settings", 100, 0));

        Assert.IsEmpty(ChromiumHistoryReader.Read(dir.Path));
    }

    [TestMethod]
    public void Read_SortKeyIsLastVisitTime()
    {
        using var dir = new TempDirectory();
        CreateHistoryDb(Path.Combine(dir.Path, "History"), ("https://a.com", "A", 12345, 0));

        var entry = ChromiumHistoryReader.Read(dir.Path).Single();

        Assert.AreEqual(12345L, entry.SortKey);
        Assert.AreEqual(BrowserHistoryTime.FromChromium(12345), entry.VisitTime);
    }
}
