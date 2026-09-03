using Microsoft.Data.Sqlite;
using Lertaro.Plugins.BrowserData.Readers;

namespace Lertaro.Plugins.BrowserData.Tests.Readers;

[TestClass]
public sealed class FirefoxPlacesReaderTests
{
    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("lertaro-tests-").FullName;

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { }
        }
    }

    private sealed class PlacesDbBuilder : IDisposable
    {
        private readonly SqliteConnection _conn;
        private int _nextPlaceId = 1;

        public PlacesDbBuilder(string path)
        {
            _conn = new SqliteConnection($"Data Source={path}");
            _conn.Open();
            using var create = _conn.CreateCommand();
            create.CommandText = """
                CREATE TABLE moz_places (id INTEGER PRIMARY KEY, url TEXT, title TEXT, last_visit_date INTEGER, hidden INTEGER);
                CREATE TABLE moz_bookmarks (id INTEGER PRIMARY KEY, type INTEGER, fk INTEGER, title TEXT);
                """;
            create.ExecuteNonQuery();
        }

        public int AddPlace(string url, string? title, long? lastVisitDate, int hidden = 0)
        {
            var id = _nextPlaceId++;
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "INSERT INTO moz_places (id, url, title, last_visit_date, hidden) VALUES ($id, $url, $title, $lv, $hidden)";
            cmd.Parameters.AddWithValue("$id", id);
            cmd.Parameters.AddWithValue("$url", url);
            cmd.Parameters.AddWithValue("$title", (object?)title ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$lv", (object?)lastVisitDate ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$hidden", hidden);
            cmd.ExecuteNonQuery();
            return id;
        }

        public void AddBookmark(int placeId, string? title)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "INSERT INTO moz_bookmarks (type, fk, title) VALUES (1, $fk, $title)";
            cmd.Parameters.AddWithValue("$fk", placeId);
            cmd.Parameters.AddWithValue("$title", (object?)title ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }

        public void Dispose() => _conn.Dispose();
    }

    [TestMethod]
    public void Read_NoPlacesFile_ReturnsEmptyBookmarksAndHistory()
    {
        using var dir = new TempDirectory();

        var (bookmarks, history) = FirefoxPlacesReader.Read(dir.Path);

        Assert.IsEmpty(bookmarks);
        Assert.IsEmpty(history);
    }

    [TestMethod]
    public void Read_BookmarkedPlace_AppearsOnlyInBookmarks()
    {
        using var dir = new TempDirectory();
        var dbPath = Path.Combine(dir.Path, "places.sqlite");
        using (var db = new PlacesDbBuilder(dbPath))
        {
            var id = db.AddPlace("https://example.com", "Example", lastVisitDate: null);
            db.AddBookmark(id, "My Bookmark");
        }

        var (bookmarks, history) = FirefoxPlacesReader.Read(dir.Path);

        Assert.HasCount(1, bookmarks);
        Assert.AreEqual("My Bookmark", bookmarks[0].Title);
        Assert.IsNull(bookmarks[0].VisitTime);
        Assert.IsEmpty(history);
    }

    [TestMethod]
    public void Read_VisitedPlaceWithNoBookmark_AppearsOnlyInHistory()
    {
        using var dir = new TempDirectory();
        var dbPath = Path.Combine(dir.Path, "places.sqlite");
        using (var db = new PlacesDbBuilder(dbPath))
        {
            db.AddPlace("https://example.com", "Example", lastVisitDate: 12345);
        }

        var (bookmarks, history) = FirefoxPlacesReader.Read(dir.Path);

        Assert.IsEmpty(bookmarks);
        Assert.HasCount(1, history);
        Assert.AreEqual(12345L, history[0].SortKey);
        Assert.AreEqual(BrowserHistoryTime.FromFirefox(12345), history[0].VisitTime);
    }

    [TestMethod]
    public void Read_EmptyBookmarkTitle_FallsBackToPlaceTitle()
    {
        using var dir = new TempDirectory();
        var dbPath = Path.Combine(dir.Path, "places.sqlite");
        using (var db = new PlacesDbBuilder(dbPath))
        {
            var id = db.AddPlace("https://example.com", "Place Title", lastVisitDate: null);
            db.AddBookmark(id, "");
        }

        var (bookmarks, _) = FirefoxPlacesReader.Read(dir.Path);

        Assert.AreEqual("Place Title", bookmarks.Single().Title);
    }

    [TestMethod]
    public void Read_HiddenPlace_ExcludedFromHistory()
    {
        using var dir = new TempDirectory();
        var dbPath = Path.Combine(dir.Path, "places.sqlite");
        using (var db = new PlacesDbBuilder(dbPath))
        {
            db.AddPlace("https://example.com", "Example", lastVisitDate: 100, hidden: 1);
        }

        var (_, history) = FirefoxPlacesReader.Read(dir.Path);

        Assert.IsEmpty(history);
    }

    [TestMethod]
    public void Read_NonHttpPlace_ExcludedFromBothLists()
    {
        using var dir = new TempDirectory();
        var dbPath = Path.Combine(dir.Path, "places.sqlite");
        using (var db = new PlacesDbBuilder(dbPath))
        {
            var id = db.AddPlace("about:config", "Config", lastVisitDate: 100);
            db.AddBookmark(id, "Config Bookmark");
        }

        var (bookmarks, history) = FirefoxPlacesReader.Read(dir.Path);

        Assert.IsEmpty(bookmarks);
        Assert.IsEmpty(history);
    }
}
