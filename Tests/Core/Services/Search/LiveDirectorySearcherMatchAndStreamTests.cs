using Lertaro.Core.Services.Search;

namespace Lertaro.Core.Tests.Services.Search;

// The MatchAndStream half of LiveDirectorySearcher: matching an already-scanned entry list against the live
// filter, including the regex clauses that travel beside it. Split from LiveDirectorySearcherTests (which
// covers the ScanDirectory walk) purely to keep both files under the repository's per-file line limit --
// the two entry points are independent, so the split costs nothing in setup.
[TestClass]
public sealed class LiveDirectorySearcherMatchAndStreamTests
{
    [TestMethod]
    public void MatchAndStream_EmptyEntries_ReturnsFalse()
    {
        var streamed = new List<SearchResult>();

        var found = LiveDirectorySearcher.MatchAndStream(new List<SearchResult>(), "query", streamed.Add, CancellationToken.None);

        Assert.IsFalse(found);
        Assert.IsEmpty(streamed);
    }

    [TestMethod]
    public void MatchAndStream_NoQuery_StreamsEveryEntry()
    {
        var entries = new List<SearchResult>
        {
            new() { Name = "alpha.txt", Path = @"C:\alpha.txt" },
            new() { Name = "beta.txt", Path = @"C:\beta.txt" },
        };
        var streamed = new List<SearchResult>();

        var found = LiveDirectorySearcher.MatchAndStream(entries, "", streamed.Add, CancellationToken.None);

        Assert.IsTrue(found);
        Assert.HasCount(2, streamed);
    }

    [TestMethod]
    public void MatchAndStream_QueryMatchesSubsequence_StreamsOnlyMatchingEntries()
    {
        var entries = new List<SearchResult>
        {
            new() { Name = "readme.txt", Path = @"C:\readme.txt" },
            new() { Name = "other.log", Path = @"C:\other.log" },
        };
        var streamed = new List<SearchResult>();

        var found = LiveDirectorySearcher.MatchAndStream(entries, "read", streamed.Add, CancellationToken.None);

        Assert.IsTrue(found);
        Assert.HasCount(1, streamed);
        Assert.AreEqual("readme.txt", streamed[0].Name);
    }

    [TestMethod]
    public void MatchAndStream_QueryMatchesNothing_ReturnsFalse()
    {
        var entries = new List<SearchResult> { new() { Name = "readme.txt", Path = @"C:\readme.txt" } };
        var streamed = new List<SearchResult>();

        var found = LiveDirectorySearcher.MatchAndStream(entries, "zzz", streamed.Add, CancellationToken.None);

        Assert.IsFalse(found);
        Assert.IsEmpty(streamed);
    }

    [TestMethod]
    public void MatchAndStream_OnlyDirectChildren_FiltersOutGrandchildren()
    {
        var entries = new List<SearchResult>
        {
            new() { Name = "child.txt", Path = @"C:\root\child.txt" },
            new() { Name = "grandchild.txt", Path = @"C:\root\sub\grandchild.txt" },
        };
        var streamed = new List<SearchResult>();

        var found = LiveDirectorySearcher.MatchAndStream(entries, "", streamed.Add, CancellationToken.None,
            onlyDirectChildren: true, parentPath: @"C:\root");

        Assert.IsTrue(found);
        Assert.HasCount(1, streamed);
        Assert.AreEqual("child.txt", streamed[0].Name);
    }

    [TestMethod]
    public void MatchAndStream_RegexOnly_StreamsOnlyMatchingEntries()
    {
        // The path-mode branch hands over a clause with no query text at all, so the clause is the whole
        // filter: building no pattern there streamed every child of a directory the index does not cover.
        var entries = new List<SearchResult>
        {
            new() { Name = "readme.md", Path = @"C:\readme.md" },
            new() { Name = "notes.txt", Path = @"C:\notes.txt" },
        };
        var streamed = new List<SearchResult>();

        var found = LiveDirectorySearcher.MatchAndStream(entries, "", streamed.Add, CancellationToken.None,
            regexes: [@"\.md$"]);

        Assert.IsTrue(found);
        Assert.HasCount(1, streamed);
        Assert.AreEqual("readme.md", streamed[0].Name);
    }

    [TestMethod]
    public void MatchAndStream_QueryTextAndRegex_RequireBoth()
    {
        // ANDed, over clause-free text (the caller splits the clauses out), so neither may shadow the other.
        var entries = new List<SearchResult>
        {
            new() { Name = "readme.md", Path = @"C:\readme.md" },
            new() { Name = "readme.txt", Path = @"C:\readme.txt" },
            new() { Name = "other.md", Path = @"C:\other.md" },
        };
        var streamed = new List<SearchResult>();

        var found = LiveDirectorySearcher.MatchAndStream(entries, "read", streamed.Add, CancellationToken.None,
            regexes: [@"\.md$"]);

        Assert.IsTrue(found);
        Assert.HasCount(1, streamed);
        Assert.AreEqual("readme.md", streamed[0].Name);
    }
}
