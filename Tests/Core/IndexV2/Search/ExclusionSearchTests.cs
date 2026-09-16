using Lertaro.Core.IndexV2.Search;

namespace Lertaro.Core.Tests.IndexV2.Search;

// The OR-plus-exclusion shape end to end, over the ASCII fast path the searcher actually uses for
// ordinary names. A disjunction that mixes a positive term with ":term" has to read the same whichever
// way round it was typed, and must still drop a name that satisfies neither side.
[TestClass]
public sealed class ExclusionSearchTests
{
    private static LiveIndexFixture BuildReportsDrive() => LiveIndexFixture.Build("T", new[]
    {
        LiveIndexFixture.Root(),
        new FileRecord(2, 1, "report-temp.txt", FileRecordFlags.None),
        new FileRecord(3, 1, "report-final.txt", FileRecordFlags.None),
        new FileRecord(4, 1, "notes.txt", FileRecordFlags.None),
        new FileRecord(5, 1, "draft-temp.txt", FileRecordFlags.None),
    });

    private static List<string> Search(LiveIndexFixture fixture, string query, int limit = 10)
    {
        var results = new List<SearchResult>();
        IndexV2Searcher.SearchStreaming(fixture.Index, query, limit, results.Add, CancellationToken.None);
        return results.Select(r => r.Path).ToList();
    }

    [TestMethod]
    public void SearchStreaming_OrWithExclusion_KeepsNamesSatisfyingEitherSide()
    {
        using var fixture = BuildReportsDrive();

        var paths = Search(fixture, "report | :temp");

        Assert.Contains(@"T:\report-temp.txt", paths);
        Assert.Contains(@"T:\report-final.txt", paths);
        Assert.Contains(@"T:\notes.txt", paths);
        Assert.DoesNotContain(@"T:\draft-temp.txt", paths);
    }

    [TestMethod]
    public void SearchStreaming_OrWithExclusion_DoesNotDependOnTermOrder()
    {
        using var fixture = BuildReportsDrive();

        var exclusionFirst = Search(fixture, ":temp | report");

        Assert.Contains(@"T:\report-temp.txt", exclusionFirst);
        Assert.Contains(@"T:\report-final.txt", exclusionFirst);
        Assert.Contains(@"T:\notes.txt", exclusionFirst);
        Assert.DoesNotContain(@"T:\draft-temp.txt", exclusionFirst);
    }

    [TestMethod]
    public void SearchStreaming_RegexWithAnExclusion_RequiresBoth()
    {
        using var fixture = BuildReportsDrive();

        // A "/.../" clause is a positive requirement in its own right, so it satisfies the exclusion-only
        // guard alone -- and the exclusion beside it must still veto. Every name here is pure ASCII, so
        // this also pins that the byte fast path steps aside for the clause instead of answering on the
        // exclusion alone (which would return every name lacking "temp").
        var paths = Search(fixture, @"/\.txt$/ :temp");

        Assert.Contains(@"T:\report-final.txt", paths);
        Assert.Contains(@"T:\notes.txt", paths);
        Assert.DoesNotContain(@"T:\report-temp.txt", paths);
        Assert.DoesNotContain(@"T:\draft-temp.txt", paths);
    }
}
