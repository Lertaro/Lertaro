using System.IO;
using Microsoft.Data.Sqlite;
using Lertaro.Plugins.BrowserData.Readers;

namespace Lertaro.Plugins.BrowserData.Tests.Readers;

[TestClass]
public sealed class FirefoxFaviconReaderTests
{
    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("lertaro-tests-").FullName;

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { }
        }
    }

    [TestMethod]
    public void Read_ModernFirefoxSchemaMapsPageToIconBytes()
    {
        using var dir = new TempDirectory();
        var imageData = new byte[] { 4, 5, 6 };
        using (var conn = new SqliteConnection($"Data Source={Path.Combine(dir.Path, "favicons.sqlite")};Pooling=False"))
        {
            conn.Open();
            using var create = conn.CreateCommand();
            create.CommandText = "CREATE TABLE moz_pages_w_icons (id INTEGER PRIMARY KEY, page_url TEXT); CREATE TABLE moz_icons (id INTEGER PRIMARY KEY, data BLOB, width INTEGER); CREATE TABLE moz_icons_to_pages (page_id INTEGER, icon_id INTEGER);";
            create.ExecuteNonQuery();
            using var insert = conn.CreateCommand();
            insert.CommandText = "INSERT INTO moz_pages_w_icons (id, page_url) VALUES (1, 'https://mozilla.org/'); INSERT INTO moz_icons (id, data, width) VALUES (9, $data, 32); INSERT INTO moz_icons_to_pages (page_id, icon_id) VALUES (1, 9);";
            insert.Parameters.AddWithValue("$data", imageData);
            insert.ExecuteNonQuery();
        }

        var result = FirefoxFaviconReader.Read(dir.Path);

        CollectionAssert.AreEqual(imageData, result["https://mozilla.org/"]);
    }
}
