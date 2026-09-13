using System.IO;
using Microsoft.Data.Sqlite;
using Lertaro.Plugins.BrowserData.Readers;

namespace Lertaro.Plugins.BrowserData.Tests.Readers;

[TestClass]
public sealed class ChromiumFaviconReaderTests
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
    public void Read_MapsPageUrlToFaviconBytesFromLiveDatabase()
    {
        using var dir = new TempDirectory();
        var imageData = new byte[] { 1, 2, 3 };
        using (var conn = new SqliteConnection($"Data Source={Path.Combine(dir.Path, "Favicons")}"))
        {
            conn.Open();
            using var create = conn.CreateCommand();
            create.CommandText = "CREATE TABLE icon_mapping (id INTEGER PRIMARY KEY, page_url TEXT, icon_id INTEGER); CREATE TABLE favicon_bitmaps (id INTEGER PRIMARY KEY, icon_id INTEGER, image_data BLOB, width INTEGER, height INTEGER);";
            create.ExecuteNonQuery();
            using var insert = conn.CreateCommand();
            insert.CommandText = "INSERT INTO icon_mapping (page_url, icon_id) VALUES ('https://example.com/', 7); INSERT INTO favicon_bitmaps (icon_id, image_data, width, height) VALUES (7, $data, 32, 32);";
            insert.Parameters.AddWithValue("$data", imageData);
            insert.ExecuteNonQuery();
        }

        var result = ChromiumFaviconReader.Read(dir.Path);

        CollectionAssert.AreEqual(imageData, result["https://example.com/"]);
    }

    [TestMethod]
    public void Read_MissingDatabase_ReturnsEmpty()
    {
        using var dir = new TempDirectory();

        Assert.IsEmpty(ChromiumFaviconReader.Read(dir.Path));
    }
}
