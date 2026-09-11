using System.IO;
using Lertaro.Core;
using Lertaro.App.ViewModels.Search.Mapping;

namespace Lertaro.App.Tests.ViewModels.Search.Mapping;

// Split out from SearchResultMapperTests to keep the test files under the repository's 300-line limit.
[TestClass]
public sealed class DirectorySelfExclusionTests
{
    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("lertaro-tests-").FullName;

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { }
        }
    }

    private static SearchResult Result(string path) => new() { Name = Path.GetFileName(path), Path = path };

    [TestMethod]
    public void RemoveQueriedDirectoryItself_DriveRootQuery_RemovesMatchingEntry()
    {
        var results = new List<SearchResult> { Result(@"C:\"), Result(@"C:\other.txt") };

        SearchResultMapper.RemoveQueriedDirectoryItself(results, @"C:\");

        Assert.HasCount(1, results);
        Assert.AreEqual(@"C:\other.txt", results[0].Path);
    }

    [TestMethod]
    public void RemoveQueriedDirectoryItself_PlainFileNameQuery_RemovesNothing()
    {
        var results = new List<SearchResult> { Result(@"C:\report.txt") };

        SearchResultMapper.RemoveQueriedDirectoryItself(results, "report");

        Assert.HasCount(1, results);
    }

    [TestMethod]
    public void RemoveQueriedDirectoryItself_ExistingDirectoryWithTrailingSeparator_RemovesMatchingEntry()
    {
        using var dir = new TempDirectory();
        var results = new List<SearchResult> { Result(dir.Path) };

        SearchResultMapper.RemoveQueriedDirectoryItself(results, dir.Path + @"\");

        Assert.IsEmpty(results);
    }

    [TestMethod]
    public void RemoveQueriedDirectoryItself_NonExistentPathWithTrailingSeparator_RemovesNothing()
    {
        var results = new List<SearchResult> { Result(@"Z:\definitely-not-real-lertaro-dir\") };

        SearchResultMapper.RemoveQueriedDirectoryItself(results, @"Z:\definitely-not-real-lertaro-dir\");

        Assert.HasCount(1, results);
    }

    [TestMethod]
    public void RemoveQueriedDirectoryItself_NullResults_DoesNotThrow() =>
        SearchResultMapper.RemoveQueriedDirectoryItself(null, @"C:\");

    [TestMethod]
    public void RemoveQueriedDirectoryItself_EmptyQuery_RemovesNothing()
    {
        var results = new List<SearchResult> { Result(@"C:\a.txt") };

        SearchResultMapper.RemoveQueriedDirectoryItself(results, "");

        Assert.HasCount(1, results);
    }

    [TestMethod]
    public void IsQueriedDirectoryItself_DriveRootQueryMatchingPath_ReturnsTrue() =>
        Assert.IsTrue(SearchResultMapper.IsQueriedDirectoryItself(@"C:\", @"C:\"));

    [TestMethod]
    public void IsQueriedDirectoryItself_WslDirectoryQuery_ReturnsTrue() =>
        Assert.IsTrue(SearchResultMapper.IsQueriedDirectoryItself(
            @"\\wsl$\Ubuntu\home\testuser", @"\\wsl$\Ubuntu\home\testuser\"));

    [TestMethod]
    public void IsQueriedDirectoryItself_NonMatchingPath_ReturnsFalse() =>
        Assert.IsFalse(SearchResultMapper.IsQueriedDirectoryItself(@"C:\other.txt", @"C:\"));

    [TestMethod]
    public void IsQueriedDirectoryItself_PlainFileQuery_ReturnsFalse() =>
        Assert.IsFalse(SearchResultMapper.IsQueriedDirectoryItself(@"C:\report.txt", "report"));
}
