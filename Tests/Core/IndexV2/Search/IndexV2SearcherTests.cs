using Lertaro.Core.IndexV2.Search;
using static Lertaro.Core.Tests.IndexV2.Search.IndexV2SearcherFixture;

namespace Lertaro.Core.Tests.IndexV2.Search;

// Name-mode search over an index snapshot: matching, the "/.../" clause, the directory filter and the
// result limit. Path-mode queries live in IndexV2SearcherPathModeTests.
[TestClass]
public sealed class IndexV2SearcherTests
{
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

    // End-to-end for the two defects that made "lertaro /\.exe$/" return nothing. Both have to be
    // fixed for this to pass: the escaped dot's backslash used to flip the query into PATH mode (so the
    // whole text was read as a path that cannot exist), and the ASCII fast path cannot apply a regex, so
    // a regex-only clause used to reject every ASCII name. "install.exe" is pure ASCII, which is exactly
    // the case that took the broken branch.
    [TestMethod]
    public void SearchStreaming_RegexOnlyQuery_FiltersByTheClause()
    {
        using var fixture = BuildSampleDrive();

        var results = RunSearch(fixture, @"/\.exe$/");

        Assert.HasCount(1, results);
        Assert.AreEqual("install.exe", results[0].Name);
    }

    [TestMethod]
    public void SearchStreaming_RegexWithANameTerm_RequiresBoth()
    {
        using var fixture = BuildSampleDrive();

        // The term narrows the prefilter, the clause is the final say: "readme.txt" has the term but does
        // not match the regex, and nothing else matches both.
        Assert.IsEmpty(RunSearch(fixture, @"readme /\.exe$/"));
    }

    // The regex prefilter must never be the thing that decides a result. Its literal is required of every
    // match, so a name that satisfies the regex while carrying the literal in an unexpected place -- here
    // the ".txt" the literal comes from is in the middle, not at the end -- must still come back.
    [TestMethod]
    public void SearchStreaming_RegexLiteralInTheMiddle_StillMatches()
    {
        using var fixture = LiveIndexFixture.Build("C", new[]
        {
            LiveIndexFixture.Root(),
            new FileRecord(2, 1, "report.txt.bak", FileRecordFlags.None),
            new FileRecord(3, 1, "notes.log", FileRecordFlags.None),
        });

        var results = RunSearch(fixture, @"/\.txt/");

        Assert.HasCount(1, results);
        Assert.AreEqual("report.txt.bak", results[0].Name);
    }

    // A clause with no extractable literal (an alternation) cannot narrow anything, and must not thereby
    // filter everything out -- the search falls back to testing the regex against every name.
    [TestMethod]
    public void SearchStreaming_RegexWithoutALiteral_StillMatches()
    {
        using var fixture = BuildSampleDrive();

        var results = RunSearch(fixture, @"/^(readme|notes)\.(txt|md)$/");

        CollectionAssert.AreEquivalent(new[] { "readme.txt", "notes.md" }, results.Select(r => r.Name).ToList());
    }

    // A regex that matches nothing still returns nothing once the mask is in play, i.e. the prefilter did
    // not turn into a "match everything" path.
    [TestMethod]
    public void SearchStreaming_RegexLiteralPresentButRegexFails_ReturnsNothing()
    {
        using var fixture = BuildSampleDrive();

        // ".txt" is present on readme.txt, so it survives the mask, and the regex then rejects it.
        Assert.IsEmpty(RunSearch(fixture, @"/^zzz.*\.txt$/"));
    }

    // A negative assertion must not become a required literal. The prefilter demands every character of the
    // literal, so claiming the asserted text inverted the lookahead and rejected the very names the pattern
    // accepts -- a miss in the mask never reaches the regex engine that would have said yes.
    [TestMethod]
    public void SearchStreaming_NegativeLookaheadQuery_DoesNotDropTheNamesTheRegexAccepts()
    {
        using var fixture = BuildSampleDrive();

        var results = RunSearch(fixture, @"/^(?!readme).*\.md$/");

        Assert.HasCount(1, results);
        Assert.AreEqual("notes.md", results[0].Name);
    }

    [TestMethod]
    public void SearchStreaming_DirectoryFilter_OnlyReturnsResultsUnderThatDirectory()
    {
        using var fixture = BuildSampleDrive();

        // Scope to Downloads only via the directory filter: "readme.txt" is under Projects and must not
        // leak in even though the term would match it.
        var results = RunSearch(fixture, "install", directoryFilter: @"C:\Downloads");

        Assert.HasCount(1, results);
        Assert.AreEqual("install.exe", results[0].Name);
    }

    [TestMethod]
    public void SearchStreaming_DirectoryFilterExcludesMatch_ReturnsNothing()
    {
        using var fixture = BuildSampleDrive();

        Assert.IsEmpty(RunSearch(fixture, "readme", directoryFilter: @"C:\Downloads"));
    }

    [TestMethod]
    public void SearchStreaming_UnresolvedDirectoryFilter_DoesNotAdmitItsNearestAncestor()
    {
        using var fixture = BuildSampleDrive();

        // The indexed ancestor resolves, but the final path segment does not. The fallback path-prefix
        // check must keep results under Projects from leaking into the nonexistent child directory.
        Assert.IsEmpty(RunSearch(fixture, "readme", directoryFilter: @"C:\Projects\missing"));
    }

    [TestMethod]
    public void SearchStreaming_ForeignDrivePrefix_ReturnsNothing()
    {
        using var fixture = BuildSampleDrive();

        Assert.IsEmpty(RunSearch(fixture, "d:readme"));
    }

    [TestMethod]
    public void SearchStreaming_BareDrivePrefixNoTerms_MatchesEverything()
    {
        using var fixture = BuildSampleDrive();

        // Root + Projects + readme.txt + notes.md + Downloads + install.exe = 6, but the self-parented
        // root row (empty name) never matches any real query -- 5 real entries are expected.
        Assert.HasCount(5, RunSearch(fixture, "c:"));
    }

    [TestMethod]
    public void SearchStreaming_NoMatch_ReturnsNothing()
    {
        using var fixture = BuildSampleDrive();

        Assert.IsEmpty(RunSearch(fixture, "zzz_no_such_thing"));
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
        var results = RunSearch(fixture, @"c:\projects\newdir\");

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

        var results = RunSearch(fixture, "inner", directoryFilter: @"C:\Projects\newdir");

        Assert.HasCount(1, results);
        Assert.AreEqual(@"C:\Projects\newdir\inner.txt", results[0].Path);
    }
}
