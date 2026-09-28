using Lertaro.Plugins.BrowserData.Readers;

using System.IO;

namespace Lertaro.Plugins.BrowserData.Tests.Readers;

[TestClass]
public sealed class ChromiumBookmarksReaderTests
{
    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("lertaro-tests-").FullName;

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { }
        }
    }

    private static string WriteBookmarksFile(TempDirectory dir, string json, string fileName = "Bookmarks")
    {
        var path = Path.Combine(dir.Path, fileName);
        File.WriteAllText(path, json);
        return path;
    }

    [TestMethod]
    public void Read_NoBookmarksFile_ReturnsEmpty()
    {
        using var dir = new TempDirectory();

        Assert.IsEmpty(ChromiumBookmarksReader.Read(dir.Path));
    }

    // Bookmarks.bak is what Chrome/Edge leave behind when they rewrite Bookmarks, so it is the candidate
    // substitute when Bookmarks cannot answer. These pin the whole rule: use it when needed, never when
    // Bookmarks can, and only for a file that actually holds a tree.

    [TestMethod]
    public void Read_NoBookmarksFile_ReadsTheBakCopy()
    {
        using var dir = new TempDirectory();
        WriteBookmarksFile(dir, """
        { "roots": { "bookmark_bar": { "type": "folder", "children": [
            { "type": "url", "name": "From Backup", "url": "https://bak.example.com" }
        ] } } }
        """, fileName: "Bookmarks.bak");

        var entry = ChromiumBookmarksReader.Read(dir.Path).Single();

        Assert.AreEqual("From Backup", entry.Title);
        Assert.IsTrue(entry.IsBookmark);
    }

    [TestMethod]
    public void Read_BothFilesPresent_NeverReadsTheBakCopy()
    {
        using var dir = new TempDirectory();
        WriteBookmarksFile(dir, """
        { "roots": { "bookmark_bar": { "type": "folder", "children": [
            { "type": "url", "name": "Current", "url": "https://live.example.com" }
        ] } } }
        """);
        // The .bak holds the PREVIOUS contents, so reading it while Bookmarks is intact would resurrect a
        // bookmark the user already deleted.
        WriteBookmarksFile(dir, """
        { "roots": { "bookmark_bar": { "type": "folder", "children": [
            { "type": "url", "name": "Deleted", "url": "https://gone.example.com" }
        ] } } }
        """, fileName: "Bookmarks.bak");

        var entries = ChromiumBookmarksReader.Read(dir.Path);

        CollectionAssert.AreEqual(new[] { "Current" }, entries.Select(e => e.Title).ToList());
    }

    [TestMethod]
    public void Read_BookmarksFileUnparsable_FallsBackToTheBakCopy()
    {
        using var dir = new TempDirectory();
        // The interrupted-write case the backup exists for: the file is there, and is not a tree.
        WriteBookmarksFile(dir, "{ not valid json");
        WriteBookmarksFile(dir, """
        { "roots": { "bookmark_bar": { "type": "folder", "children": [
            { "type": "url", "name": "Recovered", "url": "https://recovered.example.com" }
        ] } } }
        """, fileName: "Bookmarks.bak");

        var entry = ChromiumBookmarksReader.Read(dir.Path).Single();

        Assert.AreEqual("Recovered", entry.Title);
    }

    [TestMethod]
    public void Read_BakCopyWithNoRoots_YieldsNothing()
    {
        using var dir = new TempDirectory();
        WriteBookmarksFile(dir, """{ "version": 1 }""", fileName: "Bookmarks.bak");

        Assert.IsEmpty(ChromiumBookmarksReader.Read(dir.Path));
    }

    [TestMethod]
    public void Read_UrlNodesAtRootAndNestedInFolder_AreBothExtracted()
    {
        using var dir = new TempDirectory();
        WriteBookmarksFile(dir, """
        {
          "roots": {
            "bookmark_bar": {
              "type": "folder",
              "children": [
                { "type": "url", "name": "Example", "url": "https://example.com" },
                { "type": "folder", "name": "Sub", "children": [
                    { "type": "url", "name": "Nested", "url": "https://nested.example.com" }
                ]}
              ]
            },
            "other": { "type": "folder", "children": [] }
          }
        }
        """);

        var entries = ChromiumBookmarksReader.Read(dir.Path);

        Assert.HasCount(2, entries);
        Assert.IsTrue(entries.All(e => e.IsBookmark));
        CollectionAssert.AreEquivalent(new[] { "Example", "Nested" }, entries.Select(e => e.Title).ToList());
        CollectionAssert.AreEquivalent(new[] { "https://example.com", "https://nested.example.com" }, entries.Select(e => e.Url).ToList());
    }

    [TestMethod]
    public void Read_UrlNodeWithNoName_FallsBackToUrlAsTitle()
    {
        using var dir = new TempDirectory();
        WriteBookmarksFile(dir, """
        { "roots": { "bookmark_bar": { "type": "folder", "children": [
            { "type": "url", "url": "https://example.com" }
        ] } } }
        """);

        var entry = ChromiumBookmarksReader.Read(dir.Path).Single();

        Assert.AreEqual("https://example.com", entry.Title);
    }

    [TestMethod]
    public void Read_NonHttpUrlNode_IsExcluded()
    {
        using var dir = new TempDirectory();
        WriteBookmarksFile(dir, """
        { "roots": { "bookmark_bar": { "type": "folder", "children": [
            { "type": "url", "name": "Settings", "url": "chrome://settings" }
        ] } } }
        """);

        Assert.IsEmpty(ChromiumBookmarksReader.Read(dir.Path));
    }

    [TestMethod]
    public void Read_MalformedJson_ReturnsEmptyWithoutThrowing()
    {
        using var dir = new TempDirectory();
        WriteBookmarksFile(dir, "{ not valid json");

        Assert.IsEmpty(ChromiumBookmarksReader.Read(dir.Path));
    }

    [TestMethod]
    public void Read_SortKeyReflectsInsertionOrder()
    {
        using var dir = new TempDirectory();
        WriteBookmarksFile(dir, """
        { "roots": { "bookmark_bar": { "type": "folder", "children": [
            { "type": "url", "name": "First", "url": "https://a.com" },
            { "type": "url", "name": "Second", "url": "https://b.com" }
        ] } } }
        """);

        var entries = ChromiumBookmarksReader.Read(dir.Path);

        Assert.AreEqual(0, entries.Single(e => e.Title == "First").SortKey);
        Assert.AreEqual(1, entries.Single(e => e.Title == "Second").SortKey);
    }
}
