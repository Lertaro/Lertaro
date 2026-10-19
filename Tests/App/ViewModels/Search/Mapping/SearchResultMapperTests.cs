using Lertaro.Core.SearchIndex;
using Lertaro.App.ViewModels.Search.Mapping;

namespace Lertaro.App.Tests.ViewModels.Search.Mapping;

[TestClass]
public sealed class RankAndDedupeTests
{
    private static AppSearchResult Result(string path, bool isDir = false) =>
        new() { FullPath = path, Name = path, IsDir = isDir };

    private static SearchResultMapper.RankedCandidate Candidate(
        string path,
        bool isFavorite = false,
        int historyCount = 0,
        int historyPriority = int.MaxValue,
        int typeRank = int.MaxValue,
        int start = 0,
        double coverage = 1.0,
        double contiguity = 1.0,
        int tier = MatchRank.TierName,
        bool isDir = false,
        string? normalizedPath = null,
        bool isHistoryOnly = false) =>
        new(Result(path, isDir), isFavorite, historyCount, historyPriority, typeRank,
            new MatchRank(tier, start, coverage, contiguity), normalizedPath ?? path, isHistoryOnly);

    [TestMethod]
    public void RankAndDedupe_LeftContiguousBeatsLaterStart()
    {
        var ranked = SearchResultMapper.RankAndDedupe(new List<SearchResultMapper.RankedCandidate>
        {
            Candidate(@"C:\later", start: 5),
            Candidate(@"C:\at-start"),
        });

        Assert.AreEqual(@"C:\at-start", ranked[0].FullPath);
    }

    [TestMethod]
    public void RankAndDedupe_ContiguityBeatsEarlierScattered()
    {
        var ranked = SearchResultMapper.RankAndDedupe(new List<SearchResultMapper.RankedCandidate>
        {
            Candidate(@"C:\scattered", start: 0, coverage: 1.0, contiguity: 0.4),
            Candidate(@"C:\contiguous", start: 5, coverage: 0.5, contiguity: 1.0),
        });

        Assert.AreEqual(@"C:\contiguous", ranked[0].FullPath);
    }

    [TestMethod]
    public void RankAndDedupe_StartBeatsCoverage()
    {
        var ranked = SearchResultMapper.RankAndDedupe(new List<SearchResultMapper.RankedCandidate>
        {
            Candidate(@"C:\later-high-coverage", start: 5, coverage: 1.0),
            Candidate(@"C:\early-low-coverage", start: 1, coverage: 0.2),
        });

        Assert.AreEqual(@"C:\early-low-coverage", ranked[0].FullPath);
    }

    [TestMethod]
    public void RankAndDedupe_CoverageBeatsTier()
    {
        var ranked = SearchResultMapper.RankAndDedupe(new List<SearchResultMapper.RankedCandidate>
        {
            Candidate(@"C:\low-coverage-literal", tier: MatchRank.TierName, coverage: 0.5),
            Candidate(@"C:\full-pinyin", tier: MatchRank.TierFull, coverage: 1.0),
        });

        Assert.AreEqual(@"C:\full-pinyin", ranked[0].FullPath);
    }

    [TestMethod]
    public void RankAndDedupe_TierBreaksEqualStartAndCoverage()
    {
        var ranked = SearchResultMapper.RankAndDedupe(new List<SearchResultMapper.RankedCandidate>
        {
            Candidate(@"C:\full", tier: MatchRank.TierFull),
            Candidate(@"C:\initials", tier: MatchRank.TierInitials),
            Candidate(@"C:\literal", tier: MatchRank.TierName),
        });

        CollectionAssert.AreEqual(
            new[] { @"C:\literal", @"C:\initials", @"C:\full" },
            ranked.Select(r => r.FullPath).ToArray());
    }

    [TestMethod]
    public void RankAndDedupe_FavoriteBeatsNonFavoriteAtEqualQuality()
    {
        var ranked = SearchResultMapper.RankAndDedupe(new List<SearchResultMapper.RankedCandidate>
        {
            Candidate(@"C:\plain"),
            Candidate(@"C:\favorite", isFavorite: true),
        });

        Assert.AreEqual(@"C:\favorite", ranked[0].FullPath);
    }

    [TestMethod]
    public void RankAndDedupe_QualityBeatsFavorite()
    {
        var ranked = SearchResultMapper.RankAndDedupe(new List<SearchResultMapper.RankedCandidate>
        {
            Candidate(@"C:\favorite", isFavorite: true, start: 5, coverage: 0.3),
            Candidate(@"C:\plain", coverage: 1.0),
        });

        Assert.AreEqual(@"C:\plain", ranked[0].FullPath);
    }

    [TestMethod]
    public void RankAndDedupe_FiveOpensBeatOneOpenAtEqualQuality()
    {
        var ranked = SearchResultMapper.RankAndDedupe(new List<SearchResultMapper.RankedCandidate>
        {
            Candidate(@"C:\once", historyCount: 1),
            Candidate(@"C:\often", historyCount: 5),
        });

        Assert.AreEqual(@"C:\often", ranked[0].FullPath);
    }

    [TestMethod]
    public void RankAndDedupe_HistoryCannotBeatBetterQuality()
    {
        var ranked = SearchResultMapper.RankAndDedupe(new List<SearchResultMapper.RankedCandidate>
        {
            Candidate(@"C:\often", historyCount: 5, start: 5, coverage: 0.3),
            Candidate(@"C:\exact", historyCount: 0, coverage: 1.0),
        });

        Assert.AreEqual(@"C:\exact", ranked[0].FullPath);
    }

    [TestMethod]
    public void RankAndDedupe_HistoryOnlyRowDoesNotUseKeywordMatchAsANameMatch()
    {
        var ranked = SearchResultMapper.RankAndDedupe(new List<SearchResultMapper.RankedCandidate>
        {
            // A learned keyword match would otherwise look like a perfect name hit at start 0.
            Candidate(@"C:\ghost", start: 0, coverage: 1.0, contiguity: 1.0, isHistoryOnly: true),
            Candidate(@"C:\real", start: 5, coverage: 0.5, contiguity: 1.0),
        });

        Assert.AreEqual(@"C:\real", ranked[0].FullPath);
        Assert.AreEqual(@"C:\ghost", ranked[1].FullPath);
    }

    [TestMethod]
    public void RankAndDedupe_HistoryOnlyRowsStillSortAgainstEachOtherByHistoryBand()
    {
        var ranked = SearchResultMapper.RankAndDedupe(new List<SearchResultMapper.RankedCandidate>
        {
            Candidate(@"C:\ghost-once", isHistoryOnly: true, historyCount: 1),
            Candidate(@"C:\ghost-often", isHistoryOnly: true, historyCount: 5),
        });

        Assert.AreEqual(@"C:\ghost-often", ranked[0].FullPath);
    }

    [TestMethod]
    public void RankAndDedupe_TypeGroupBeatsABetterMatch()
    {
        var ranked = SearchResultMapper.RankAndDedupe(new List<SearchResultMapper.RankedCandidate>
        {
            Candidate(@"C:\files-perfect", typeRank: 2, start: 0, coverage: 1.0, contiguity: 1.0),
            Candidate(@"C:\apps-weak", typeRank: 1, start: 9, coverage: 0.2, contiguity: 0.4),
        });

        Assert.AreEqual(@"C:\apps-weak", ranked[0].FullPath);
    }

    [TestMethod]
    public void RankAndDedupe_InsideOneTypeTheEngineQualityDecides()
    {
        var ranked = SearchResultMapper.RankAndDedupe(new List<SearchResultMapper.RankedCandidate>
        {
            Candidate(@"C:\later", typeRank: 1, start: 5, coverage: 0.5),
            Candidate(@"C:\at-start", typeRank: 1),
        });

        Assert.AreEqual(@"C:\at-start", ranked[0].FullPath);
    }

    [TestMethod]
    public void RankAndDedupe_TypeGroupBeatsAFavoriteOfALaterType()
    {
        var ranked = SearchResultMapper.RankAndDedupe(new List<SearchResultMapper.RankedCandidate>
        {
            Candidate(@"C:\app", typeRank: 0),
            Candidate(@"C:\favorite", isFavorite: true, typeRank: 3),
        });

        Assert.AreEqual(@"C:\app", ranked[0].FullPath);
    }

    [TestMethod]
    public void RankAndDedupe_FileBeatsFolder_WhenTheMatchIsOtherwiseEqual()
    {
        var ranked = SearchResultMapper.RankAndDedupe(new List<SearchResultMapper.RankedCandidate>
        {
            Candidate(@"C:\folder", isDir: true),
            Candidate(@"C:\file"),
        });

        Assert.AreEqual(@"C:\file", ranked[0].FullPath);
    }

    [TestMethod]
    public void RankAndDedupe_NaturalNameOrderThenLength()
    {
        var ranked = SearchResultMapper.RankAndDedupe(new List<SearchResultMapper.RankedCandidate>
        {
            Candidate(@"C:\a10"),
            Candidate(@"C:\a2"),
            Candidate(@"C:\a2-long"),
        });

        CollectionAssert.AreEqual(
            new[] { @"C:\a2", @"C:\a2-long", @"C:\a10" },
            ranked.Select(r => r.FullPath).ToArray());
    }

    [TestMethod]
    public void RankAndDedupe_DuplicateNormalizedPath_KeepsTheBetterOne()
    {
        var ranked = SearchResultMapper.RankAndDedupe(new List<SearchResultMapper.RankedCandidate>
        {
            Candidate(@"C:\weak", normalizedPath: @"C:\same"),
            Candidate(@"C:\strong", isFavorite: true, normalizedPath: @"C:\same"),
        });

        Assert.HasCount(1, ranked);
        Assert.AreEqual(@"C:\strong", ranked[0].FullPath);
    }

    [TestMethod]
    public void RankAndDedupe_EmptyInput_ReturnsEmptyList() =>
        Assert.IsEmpty(SearchResultMapper.RankAndDedupe(new List<SearchResultMapper.RankedCandidate>()));
}
