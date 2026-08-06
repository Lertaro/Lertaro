using Microsoft.Data.Sqlite;

namespace Lertaro.Plugins.BrowserData.Tests;

[TestClass]
public sealed class BrowserDataCacheTests
{
    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("lertaro-tests-").FullName;

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { }
        }
    }

    private static void WriteBookmarksFile(string profileDir) => File.WriteAllText(Path.Combine(profileDir, "Bookmarks"), """
        { "roots": { "bookmark_bar": { "type": "folder", "children": [
            { "type": "url", "name": "Example", "url": "https://example.com" }
        ] } } }
        """);

    private static void WriteHistoryDb(string profileDir)
    {
        using var conn = new SqliteConnection($"Data Source={Path.Combine(profileDir, "History")}");
        conn.Open();
        using var create = conn.CreateCommand();
        create.CommandText = "CREATE TABLE urls (id INTEGER PRIMARY KEY, url TEXT, title TEXT, last_visit_time INTEGER, hidden INTEGER)";
        create.ExecuteNonQuery();
        using var insert = conn.CreateCommand();
        insert.CommandText = "INSERT INTO urls (url, title, last_visit_time, hidden) VALUES ('https://visited.com', 'Visited', 100, 0)";
        insert.ExecuteNonQuery();
    }

    private static List<BrowserProfileConfig> ProfileConfig(string path) =>
        new() { new BrowserProfileConfig { Name = "Test", Path = path } };

    [TestMethod]
    public void LoadAll_BothEnabled_ReturnsBookmarksAndHistory()
    {
        using var dir = new TempDirectory();
        WriteBookmarksFile(dir.Path);
        WriteHistoryDb(dir.Path);

        var result = BrowserDataCache.LoadAll(ProfileConfig(dir.Path), indexBookmarks: true, indexHistory: true);

        var entries = result.Single();
        Assert.HasCount(1, entries.Bookmarks);
        Assert.HasCount(1, entries.History);
    }

    [TestMethod]
    public void LoadAll_BookmarksDisabled_SkipsBookmarksButKeepsHistory()
    {
        using var dir = new TempDirectory();
        WriteBookmarksFile(dir.Path);
        WriteHistoryDb(dir.Path);

        var result = BrowserDataCache.LoadAll(ProfileConfig(dir.Path), indexBookmarks: false, indexHistory: true);

        var entries = result.Single();
        Assert.IsEmpty(entries.Bookmarks);
        Assert.HasCount(1, entries.History);
    }

    [TestMethod]
    public void LoadAll_HistoryDisabled_SkipsHistoryButKeepsBookmarks()
    {
        using var dir = new TempDirectory();
        WriteBookmarksFile(dir.Path);
        WriteHistoryDb(dir.Path);

        var result = BrowserDataCache.LoadAll(ProfileConfig(dir.Path), indexBookmarks: true, indexHistory: false);

        var entries = result.Single();
        Assert.HasCount(1, entries.Bookmarks);
        Assert.IsEmpty(entries.History);
    }

    [TestMethod]
    public void LoadAll_BothDisabled_ReturnsNoProfiles()
    {
        using var dir = new TempDirectory();
        WriteBookmarksFile(dir.Path);
        WriteHistoryDb(dir.Path);

        var result = BrowserDataCache.LoadAll(ProfileConfig(dir.Path), indexBookmarks: false, indexHistory: false);

        Assert.IsEmpty(result);
    }
}
