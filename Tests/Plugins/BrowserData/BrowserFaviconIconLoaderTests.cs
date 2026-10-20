using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Data.Sqlite;

namespace Lertaro.Plugins.BrowserData.Tests;

[TestClass]
[DoNotParallelize]
public sealed class BrowserFaviconIconLoaderTests
{
    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    private static byte[] EncodedIcon(int size = 4)
    {
        var pixels = new byte[size * size * 4];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = 0x20;
            pixels[i + 1] = 0x40;
            pixels[i + 2] = 0x80;
            pixels[i + 3] = 0xFF;
        }

        var source = BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, pixels, size * 4);
        source.Freeze();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static void WriteFaviconDb(string profileDir, string pageUrl, byte[] imageData)
    {
        using var conn = new SqliteConnection($"Data Source={Path.Combine(profileDir, "Favicons")};Pooling=False");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $$"""
            CREATE TABLE icon_mapping (page_url TEXT, icon_id INTEGER);
            CREATE TABLE favicon_bitmaps (icon_id INTEGER, width INTEGER, height INTEGER, image_data BLOB);
            INSERT INTO icon_mapping VALUES ('{{pageUrl}}', 1);
            INSERT INTO favicon_bitmaps VALUES (1, 16, 16, x'{{Convert.ToHexString(imageData)}}');
            """;
        cmd.ExecuteNonQuery();
    }

    [TestMethod]
    public void ToHBitmap_NoBytes_ReturnsZero()
    {
        Assert.AreEqual(IntPtr.Zero, BrowserFaviconIconLoader.ToHBitmap(null));
        Assert.AreEqual(IntPtr.Zero, BrowserFaviconIconLoader.ToHBitmap([]));
    }

    // Browser icon payloads are third-party bytes, so a non-image must yield no handle rather than throw.
    [TestMethod]
    public void ToHBitmap_NotAnImage_ReturnsZero() =>
        Assert.AreEqual(IntPtr.Zero, BrowserFaviconIconLoader.ToHBitmap([1, 2, 3, 4, 5]));

    [TestMethod]
    public void ToHBitmap_EncodedIcon_ReturnsDeletableHandle()
    {
        var handle = BrowserFaviconIconLoader.ToHBitmap(EncodedIcon());

        Assert.AreNotEqual(IntPtr.Zero, handle);
        Assert.IsTrue(DeleteObject(handle));
    }

    [TestMethod]
    public void LoadAll_CachesRawFaviconBytes_AndStillRendersForTheMatchingRow()
    {
        var dir = Directory.CreateTempSubdirectory("lertaro-favicon-tests-").FullName;
        try
        {
            var icon = EncodedIcon();
            File.WriteAllText(Path.Combine(dir, "Bookmarks"), """
                { "roots": { "bookmark_bar": { "type": "folder", "children": [
                    { "type": "url", "name": "Example", "url": "https://example.com" }
                ] } } }
                """);
            WriteFaviconDb(dir, "https://example.com", icon);

            var entries = BrowserDataCache.LoadAll(
                new List<BrowserProfileConfig> { new() { Name = "Test", Path = dir } },
                indexBookmarks: true,
                indexHistory: false).Single();

            var bookmark = entries.Bookmarks.Single();
            CollectionAssert.AreEqual(icon, bookmark.FaviconData);

            var handle = BrowserFaviconIconLoader.ToHBitmap(bookmark.FaviconData);
            Assert.AreNotEqual(IntPtr.Zero, handle);
            DeleteObject(handle);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }
}
