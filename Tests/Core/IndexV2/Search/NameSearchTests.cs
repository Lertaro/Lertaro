using Lertaro.Core.IndexV2.Search;

namespace Lertaro.Core.Tests.IndexV2.Search;

[TestClass]
public sealed class NameSearchTests
{
    [TestMethod]
    public void SearchStreaming_WholeNameMatchOutranksAncestorCompletedMatch()
    {
        using var fixture = LiveIndexFixture.Build("Z", new[]
        {
            LiveIndexFixture.Root(),
            new FileRecord(2, 1, "FX_Archive_Ocr", FileRecordFlags.Directory),
            new FileRecord(3, 1, "Archive", FileRecordFlags.Directory),
            new FileRecord(4, 3, "Ocr.md", FileRecordFlags.None),
        });
        var results = new List<SearchResult>();

        IndexV2Searcher.SearchStreaming(fixture.Index, "archive ocr", 10, results.Add, CancellationToken.None);

        var wholeName = results.Single(result => result.Path == @"Z:\FX_Archive_Ocr");
        var ancestorCompleted = results.Single(result => result.Path == @"Z:\Archive\Ocr.md");

        Assert.IsLessThan(0, SearchResultRankComparer.Instance.Compare(wholeName, ancestorCompleted));
    }

    [TestMethod]
    public void SearchStreaming_FileNameFilterIsAppliedBeforeTheCandidateLimit()
    {
        using var fixture = LiveIndexFixture.Build("Z", new[]
        {
            LiveIndexFixture.Root(),
            new FileRecord(2, 1, "Archive.txt", FileRecordFlags.None),
            new FileRecord(3, 1, "Archive.md", FileRecordFlags.None),
        });
        var results = new List<SearchResult>();

        IndexV2Searcher.SearchStreaming(fixture.Index, "archive", 1, results.Add, CancellationToken.None,
            fileNameFilter: "*.md");

        Assert.HasCount(1, results);
        Assert.AreEqual("Archive.md", results[0].Name);
    }
}
