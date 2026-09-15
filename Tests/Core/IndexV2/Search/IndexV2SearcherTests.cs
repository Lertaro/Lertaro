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

    // End-to-end for the two defects that made "lertaro regex:/\.exe$/" return nothing. Both have to be
    // fixed for this to pass: the escaped dot's backslash used to flip the query into PATH mode (so the
    // whole text was read as a path that cannot exist), and the ASCII fast path cannot apply a regex, so
    // a regex-only clause used to reject every ASCII name. "install.exe" is pure ASCII, which is exactly
    // the case that took the broken branch.
    [TestMethod]
    public void SearchStreaming_RegexOnlyQuery_FiltersByTheClause()
    {
        using var fixture = BuildSampleDrive();
        var results = new List<SearchResult>();

        IndexV2Searcher.SearchStreaming(fixture.Index, @"regex:/\.exe$/", 10, results.Add, CancellationToken.None);

        Assert.HasCount(1, results);
        Assert.AreEqual("install.exe", results[0].Name);
    }

    [TestMethod]
    public void SearchStreaming_RegexWithANameTerm_RequiresBoth()
    {
        using var fixture = BuildSampleDrive();
        var results = new List<SearchResult>();

        // The term narrows the prefilter, the clause is the final say: "readme.txt" has the term but does
        // not match the regex, and nothing else matches both.
        IndexV2Searcher.SearchStreaming(fixture.Index, @"readme regex:/\.exe$/", 10, results.Add, CancellationToken.None);

        Assert.IsEmpty(results);
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

    // Path mode bounded its widened refinement set with a fixed constant while NameSearch bounds the
    // identical expression by the index -- and that set is exactly what topN.Finish returns, so a
    // path-mode query the caller asked for "everything" of silently stopped at 4000 rows.
    [TestMethod]
    public void SearchStreaming_PathModeQuery_ReturnsEveryMatchAboveTheOldFixedCap()
    {
        const int fileCount = 4200;
        var records = new List<FileRecord>
        {
            LiveIndexFixture.Root(),
            new FileRecord(2, 1, "stuff", FileRecordFlags.Directory),
        };
        for (var i = 0; i < fileCount; i++)
            records.Add(new FileRecord((ulong)(3 + i), 2, $"file{i}.txt", FileRecordFlags.None));

        using var fixture = LiveIndexFixture.Build("C", records);
        var results = new List<SearchResult>();

        IndexV2Searcher.SearchStreaming(fixture.Index, @"C:\stuff\file", int.MaxValue, results.Add, CancellationToken.None);

        Assert.IsGreaterThan(4000, results.Count, $"path mode still truncates: got {results.Count} of {fileCount} matches");
    }

    [TestMethod]
    public void SearchStreaming_PathModeQuery_ListsChildrenOfAnAddedDirectory()
    {
        using var fixture = BuildSampleDrive();
        fixture.Index.Mutate((_, delta) =>
        {
            delta.Upsert(100, 2, "newdir", FileRecordFlags.Directory, 0, 0, 0, 0);
            delta.Upsert(101, 100, "inner.txt", FileRecordFlags.None, 0, 0, 0, 0);
        });
        var results = Search(fixture, @"c:\projects\newdir\");

        CollectionAssert.AreEquivalent(new[] { "newdir", "inner.txt" }, results.Select(r => r.Name).ToArray());
    }

    [TestMethod]
    public void SearchStreaming_DirectoryFilter_UsesAnAddedDirectoryPath()
    {
        using var fixture = BuildSampleDrive();
        fixture.Index.Mutate((_, delta) =>
        {
            delta.Upsert(100, 2, "newdir", FileRecordFlags.Directory, 0, 0, 0, 0);
            delta.Upsert(101, 100, "inner.txt", FileRecordFlags.None, 0, 0, 0, 0);
        });
        var results = new List<SearchResult>();

        IndexV2Searcher.SearchStreaming(fixture.Index, "inner", 10, results.Add, CancellationToken.None,
            directoryFilter: @"C:\Projects\newdir");

        Assert.HasCount(1, results);
        Assert.AreEqual(@"C:\Projects\newdir\inner.txt", results[0].Path);
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
    public void SearchStreaming_PathModeWithADriveAndNoSpace_TreatsTheColonAsLiteralText()
    {
        // "c:readme" is no longer read as a drive spec -- the colon needs a space after it to be one.
        // Here the literal "c:readme" appears in no file name, so nothing matches.
        using var fixture = BuildSampleDrive();

        var results = Search(fixture, @"projects\ c:readme");

        Assert.IsEmpty(results);
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
