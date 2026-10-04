using Lertaro.Core.IndexV2.Search;

namespace Lertaro.Core.Tests.IndexV2.Search;

// A path-mode query has to satisfy BOTH its path condition and any "/.../" clause it carries.
// Regex clauses are lifted out before the path/name decision is made -- they are full of backslashes and
// slashes, which would otherwise read as path separators -- so the clauses have to survive that decision
// and be handed back to the path matcher. Dropping them there silently returned rows the user had already
// excluded, and a regex-only path query returned everything under the folder.
[TestClass]
public sealed class PathModeRegexTests
{
    private static LiveIndexFixture BuildProjectsDrive() => LiveIndexFixture.Build("T", new[]
    {
        LiveIndexFixture.Root(),
        new FileRecord(2, 1, "projects", FileRecordFlags.Directory),
        new FileRecord(3, 2, "report.md", FileRecordFlags.None),
        new FileRecord(4, 2, "notes.txt", FileRecordFlags.None),
        new FileRecord(5, 1, "report.md", FileRecordFlags.None),
    });

    private static List<SearchResult> Search(LiveIndexFixture fixture, string query, int limit = 10)
    {
        var results = new List<SearchResult>();
        IndexV2Searcher.SearchStreaming(fixture.Index, query, limit, results.Add, CancellationToken.None);
        return results;
    }

    [TestMethod]
    public void SearchStreaming_PathModeWithRegex_RequiresThePathAndTheRegex()
    {
        using var fixture = BuildProjectsDrive();

        var results = Search(fixture, @"T:\projects\ /^report\.md$/");

        // "notes.txt" is under the path but fails the regex; the root "report.md" passes the regex but is
        // outside the path. Only the row that satisfies both may come back.
        Assert.HasCount(1, results);
        Assert.AreEqual(@"T:\projects\report.md", results[0].Path);
    }

    [TestMethod]
    public void SearchStreaming_PathModeWithRegexOnly_DoesNotReturnUnrelatedChildren()
    {
        using var fixture = BuildProjectsDrive();

        var results = Search(fixture, @"T:\projects\ /^notes\.txt$/");

        Assert.HasCount(1, results);
        Assert.AreEqual(@"T:\projects\notes.txt", results[0].Path);
    }

    [TestMethod]
    public void SearchStreaming_RelativePathModeWithRegexOnly_StillAppliesTheRegex()
    {
        using var fixture = BuildProjectsDrive();

        // No drive letter, so this takes the other branch of PathSearch: TryDirectoryChildren requires a
        // target drive and bails out, which routes the file part through PathSearchFuzzy -> the ASCII byte
        // fast path. That path cannot apply a clause at all, and with a regex-only file part it has no
        // positive term to match on either -- it used to return every ASCII child, or nothing at all.
        var results = Search(fixture, @"projects\ /^report\.md$/");

        Assert.HasCount(1, results);
        Assert.AreEqual(@"T:\projects\report.md", results[0].Path);
    }

    [TestMethod]
    public void SearchStreaming_PathModeWithoutRegex_KeepsEveryChild()
    {
        using var fixture = BuildProjectsDrive();

        var results = Search(fixture, @"T:\projects\");

        // No regex clause, so the pre-existing path behaviour must be untouched: every child of the
        // folder comes back, and the same-named file outside it still does not.
        var paths = results.Select(r => r.Path).ToList();
        Assert.Contains(@"T:\projects\report.md", paths);
        Assert.Contains(@"T:\projects\notes.txt", paths);
        Assert.DoesNotContain(@"T:\report.md", paths);
    }
}
