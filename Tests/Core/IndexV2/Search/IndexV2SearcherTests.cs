using Lertaro.Core.IndexV2.Search;

namespace Lertaro.Core.Tests.IndexV2.Search;

[TestClass]
public sealed class IndexV2SearcherTests
{
    private static LiveIndexFixture BuildSampleDrive() => LiveIndexFixture.Build("C", new[]
    {
        LiveIndexFixture.Root(),
        new FileRecord(2, 1, "Projects", FileRecordFlags.Directory),
        new FileRecord(3, 2, "readme.txt", FileRecordFlags.None),
        new FileRecord(4, 2, "notes.md", FileRecordFlags.None),
        new FileRecord(5, 1, "Downloads", FileRecordFlags.Directory),
        new FileRecord(6, 5, "install.exe", FileRecordFlags.None),
    });

    [TestMethod]
    public void SearchStreaming_NameMatch_ReturnsExpectedResult()
    {
        using var fixture = BuildSampleDrive();
        var results = new List<SearchResult>();

        IndexV2Searcher.SearchStreaming(fixture.Index, "readme", 10, results.Add, CancellationToken.None);

        Assert.HasCount(1, results);
        Assert.AreEqual("readme.txt", results[0].Name);
        Assert.AreEqual(@"C:\Projects\readme.txt", results[0].Path);
        Assert.IsFalse(results[0].IsDir);
    }

    [TestMethod]
    public void SearchStreaming_DirectoryMatch_ReturnsWithIsDirTrue()
    {
        using var fixture = BuildSampleDrive();
        var results = new List<SearchResult>();

        IndexV2Searcher.SearchStreaming(fixture.Index, "Projects", 10, results.Add, CancellationToken.None);

        Assert.HasCount(1, results);
        Assert.IsTrue(results[0].IsDir);
        Assert.AreEqual(@"C:\Projects", results[0].Path);
    }

    [TestMethod]
    public void SearchStreaming_DirectoryFilter_OnlyReturnsResultsUnderThatDirectory()
    {
        using var fixture = BuildSampleDrive();
        var results = new List<SearchResult>();

        // Both "readme.txt" (under Projects) and "install.exe" (under Downloads) would match "e"-ish
        // fuzzy terms broadly; scope to Downloads only via the directory filter.
        IndexV2Searcher.SearchStreaming(fixture.Index, "install", 10, results.Add, CancellationToken.None, directoryFilter: @"C:\Downloads");

        Assert.HasCount(1, results);
        Assert.AreEqual("install.exe", results[0].Name);
    }

    [TestMethod]
    public void SearchStreaming_DirectoryFilterExcludesMatch_ReturnsNothing()
    {
        using var fixture = BuildSampleDrive();
        var results = new List<SearchResult>();

        IndexV2Searcher.SearchStreaming(fixture.Index, "readme", 10, results.Add, CancellationToken.None, directoryFilter: @"C:\Downloads");

        Assert.IsEmpty(results);
    }

    [TestMethod]
    public void SearchStreaming_UnresolvedDirectoryFilter_DoesNotAdmitItsNearestAncestor()
    {
        using var fixture = BuildSampleDrive();
        var results = new List<SearchResult>();

        // The indexed ancestor resolves, but the final path segment does not. The fallback path-prefix
        // check must keep results under Projects from leaking into the nonexistent child directory.
        IndexV2Searcher.SearchStreaming(fixture.Index, "readme", 10, results.Add, CancellationToken.None, directoryFilter: @"C:\Projects\missing");

        Assert.IsEmpty(results);
    }

    [TestMethod]
    public void SearchStreaming_ForeignDrivePrefix_ReturnsNothing()
    {
        using var fixture = BuildSampleDrive();
        var results = new List<SearchResult>();

        IndexV2Searcher.SearchStreaming(fixture.Index, "d:readme", 10, results.Add, CancellationToken.None);

        Assert.IsEmpty(results);
    }

    [TestMethod]
    public void SearchStreaming_BareDrivePrefixNoTerms_MatchesEverything()
    {
        using var fixture = BuildSampleDrive();
        var results = new List<SearchResult>();

        IndexV2Searcher.SearchStreaming(fixture.Index, "c:", 10, results.Add, CancellationToken.None);

        // Root + Projects + readme.txt + notes.md + Downloads + install.exe = 6, but the self-parented
        // root row (empty name) never matches any real query -- 5 real entries are expected.
        Assert.HasCount(5, results);
    }

    [TestMethod]
    public void SearchStreaming_NoMatch_ReturnsNothing()
    {
        using var fixture = BuildSampleDrive();
        var results = new List<SearchResult>();

        IndexV2Searcher.SearchStreaming(fixture.Index, "zzz_no_such_thing", 10, results.Add, CancellationToken.None);

        Assert.IsEmpty(results);
    }

    [TestMethod]
    public void SearchStreaming_LimitCapsResultCount()
    {
        using var fixture = BuildSampleDrive();
        var results = new List<SearchResult>();

        IndexV2Searcher.SearchStreaming(fixture.Index, "c:", 2, results.Add, CancellationToken.None);

        Assert.HasCount(2, results);
    }

    [TestMethod]
    public void SearchStreaming_PathModeQuery_ListsDirectoryItselfPlusChildren()
    {
        // A trailing-separator path lists the resolved directory itself alongside its children (see
        // PathSearch.TryDirectoryChildren) -- not just the children.
        using var fixture = BuildSampleDrive();
        var results = new List<SearchResult>();

        IndexV2Searcher.SearchStreaming(fixture.Index, @"c:\Projects\", 10, results.Add, CancellationToken.None);

        var names = results.Select(r => r.Name).ToList();
        CollectionAssert.AreEquivalent(new[] { "Projects", "notes.md", "readme.txt" }, names);
    }

    private static List<SearchResult> Search(LiveIndexFixture fixture, string query)
    {
        var results = new List<SearchResult>();
        IndexV2Searcher.SearchStreaming(fixture.Index, query, 10, results.Add, CancellationToken.None);
        return results;
    }

    [TestMethod]
    public void SearchStreaming_PathModeWithADriveInTheFilePart_StillMatches()
    {
        // Reported case: "projects\ readme c:" came back empty. The file part was parsed by a routine
        // with no notion of a drive, so "c:" stayed an ordinary term -- and a term containing a colon can
        // never match a file name, so one anywhere in the query took the whole thing to no results.
        using var fixture = BuildSampleDrive();

        var results = Search(fixture, @"projects\ readme c:");

        Assert.HasCount(1, results);
        Assert.AreEqual("readme.txt", results[0].Name);
    }

    [TestMethod]
    public void SearchStreaming_PathModeWithAForeignDriveInTheFilePart_MatchesNothing()
    {
        // And it is a filter, not merely something to drop: naming a drive the results are not on has to
        // exclude them, the same as it does in a name-mode query.
        using var fixture = BuildSampleDrive();

        Assert.IsEmpty(Search(fixture, @"projects\ readme z:"));
    }

    [TestMethod]
    public void SearchStreaming_PathModeWithADriveAndNoSpace_StillMatches()
    {
        using var fixture = BuildSampleDrive();

        var results = Search(fixture, @"projects\ c:readme");

        Assert.HasCount(1, results);
        Assert.AreEqual("readme.txt", results[0].Name);
    }

    [TestMethod]
    public void SearchStreaming_PathModeDirectoryFilter_ExcludesAnotherDirectory()
    {
        using var fixture = BuildSampleDrive();
        var results = new List<SearchResult>();

        IndexV2Searcher.SearchStreaming(fixture.Index, @"projects\ readme", 10, results.Add,
            CancellationToken.None, directoryFilter: @"C:\Downloads");

        Assert.IsEmpty(results);
    }
}
