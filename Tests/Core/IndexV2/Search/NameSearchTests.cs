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

    // A whole-word hit later in the name ("code-atlas") beats a word-start hit at the left edge
    // ("atlasing*"); the exact/left-contiguous "atlas" still leads both.
    [TestMethod]
    public void SearchStreaming_LimitKeepsWholeWordHitsAheadOfWordStarts()
    {
        var records = new List<FileRecord>
        {
            LiveIndexFixture.Root(),
            new(2, 1, "code-atlas", FileRecordFlags.Directory),
            new(3, 1, "atlas", FileRecordFlags.Directory),
        };
        ulong id = 10;
        for (var i = 0; i < 20; i++)
        {
            records.Add(new FileRecord(id++, 1, $"atlasing{i}", FileRecordFlags.Directory));
            records.Add(new FileRecord(id++, 1, $"AutofillStates{i}", FileRecordFlags.None));
        }
        using var fixture = LiveIndexFixture.Build("Z", records.ToArray());
        var results = new List<SearchResult>();

        IndexV2Searcher.SearchStreaming(fixture.Index, "atlas", 5, results.Add, CancellationToken.None);

        var names = results.Order(SearchResultRankComparer.Instance).Select(result => result.Name).ToList();
        Assert.HasCount(5, names);
        CollectionAssert.AreEqual(new[] { "atlas", "code-atlas" }, names.Take(2).ToArray());
        Assert.IsTrue(names.Skip(2).All(name => name.StartsWith("atlasing", StringComparison.Ordinal)), string.Join(", ", names));
    }

    // Second half of the reported case: every "Atlas*" file starts at 0 and is contiguous; the later
    // "code-atlas" is not left-contiguous and must not take the cut.
    [TestMethod]
    public void SearchStreaming_LimitKeepsTheLeftContiguousHitAheadOfALaterWholeWord()
    {
        var records = new List<FileRecord>
        {
            LiveIndexFixture.Root(),
            new(2, 1, "Games", FileRecordFlags.Directory),
            new(3, 2, "Content", FileRecordFlags.Directory),
            new(4, 3, "Missions", FileRecordFlags.Directory),
            new(5, 1, "code-atlas", FileRecordFlags.Directory),
        };
        ulong id = 10;
        for (var i = 0; i < 20; i++)
            records.Add(new FileRecord(id++, 4, $"Atlas{i}.xml", FileRecordFlags.None));
        using var fixture = LiveIndexFixture.Build("Z", records.ToArray());
        var results = new List<SearchResult>();

        IndexV2Searcher.SearchStreaming(fixture.Index, "atlas", 3, results.Add, CancellationToken.None);

        Assert.IsTrue(results.Order(SearchResultRankComparer.Instance).First().Name.StartsWith("Atlas", StringComparison.Ordinal));
    }

    // Coverage is a match key; recency and depth are not. A shorter left-contiguous name with the same
    // matched length wins over a longer one.
    [TestMethod]
    public void SearchStreaming_LimitKeepsHigherCoverageAheadOfLongerNames()
    {
        var records = new List<FileRecord>
        {
            LiveIndexFixture.Root(),
            new(2, 1, "atlas-notes.txt", FileRecordFlags.None),
            new(3, 1, "atlas1.txt", FileRecordFlags.None),
        };
        ulong id = 10;
        for (var i = 0; i < 20; i++)
            records.Add(new FileRecord(id++, 1, $"atlas{i}.txt", FileRecordFlags.None));
        using var fixture = LiveIndexFixture.Build("Z", records.ToArray());
        var results = new List<SearchResult>();

        IndexV2Searcher.SearchStreaming(fixture.Index, "atlas", 3, results.Add, CancellationToken.None);

        Assert.AreEqual("atlas0.txt", results.Order(SearchResultRankComparer.Instance).First().Name);
    }

    [TestMethod]
    public void SearchStreaming_HalfWidthQueryMatchesFullWidthName()
    {
        using var fixture = LiveIndexFixture.Build("Z", new[]
        {
            LiveIndexFixture.Root(),
            new FileRecord(2, 1, "ｆａｓｔ.txt", FileRecordFlags.None),
        });
        var results = new List<SearchResult>();

        IndexV2Searcher.SearchStreaming(fixture.Index, "fast", 10, results.Add, CancellationToken.None);

        Assert.HasCount(1, results);
        Assert.AreEqual("ｆａｓｔ.txt", results[0].Name);
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
