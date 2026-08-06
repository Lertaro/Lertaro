using Lertaro.Plugins.BrowserData.Readers;

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

    private static string WriteBookmarksFile(TempDirectory dir, string json)
    {
        var path = Path.Combine(dir.Path, "Bookmarks");
        File.WriteAllText(path, json);
        return path;
    }

    [TestMethod]
    public void Read_NoBookmarksFile_ReturnsEmpty()
    {
        using var dir = new TempDirectory();

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
