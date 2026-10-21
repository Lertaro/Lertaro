using System.IO;

using Lertaro.App.ViewModels.Search.Mapping;
using Lertaro.Core;
using Lertaro.Core.SearchIndex;
using PriorityVerdict = Lertaro.Core.ExclusionRuleSet.PriorityVerdict;

namespace Lertaro.App.Tests.ViewModels.Search.Mapping;

[TestClass]
public sealed class RankedCandidateOrderingTests
{
    private static SearchResultMapper.RankedCandidate Candidate(string path, int typeRank = 0, int start = 0, double coverage = 1.0) =>
        new(
            new AppSearchResult { FullPath = path, Name = Path.GetFileName(path), ResultKind = string.Empty },
            false,
            0,
            typeRank,
            new MatchRank(MatchRank.TierName, start, coverage, 1.0),
            path);

    private static Func<string, bool, PriorityVerdict> Priorities() => (path, _) => path switch
    {
        var p when p.Contains("high", StringComparison.OrdinalIgnoreCase) => new PriorityVerdict(IndexRuleEffect.High, false),
        var p when p.Contains("low", StringComparison.OrdinalIgnoreCase) => new PriorityVerdict(IndexRuleEffect.Low, false),
        _ => new PriorityVerdict(IndexRuleEffect.Normal, false),
    };

    [TestMethod]
    public void HighPriority_SortsBeforeNormalEvenWithAWorseName()
    {
        var ranked = RankedCandidateOrdering.Order(new List<SearchResultMapper.RankedCandidate>
        {
            Candidate(@"C:\a-normal.txt"),
            Candidate(@"C:\z-high.txt"),
        }, new RankContext(Priorities()));

        Assert.AreEqual(@"C:\z-high.txt", ranked[0].Result.FullPath);
    }

    [TestMethod]
    public void LowPriority_SortsAfterNormalEvenWithABetterMatch()
    {
        var ranked = RankedCandidateOrdering.Order(new List<SearchResultMapper.RankedCandidate>
        {
            Candidate(@"C:\z-low.txt", start: 9, coverage: 0.2),
            Candidate(@"C:\a-normal.txt", start: 0, coverage: 1.0),
        }, new RankContext(Priorities()));

        Assert.AreEqual(@"C:\a-normal.txt", ranked[0].Result.FullPath);
        Assert.AreEqual(@"C:\z-low.txt", ranked[1].Result.FullPath);
    }

    [TestMethod]
    public void Priority_DominatesTypeRank()
    {
        var ranked = RankedCandidateOrdering.Order(new List<SearchResultMapper.RankedCandidate>
        {
            Candidate(@"C:\a-low.txt", typeRank: 0),
            Candidate(@"C:\z-high.txt", typeRank: 1),
        }, new RankContext(Priorities()));

        Assert.AreEqual(@"C:\z-high.txt", ranked[0].Result.FullPath);
    }

    [TestMethod]
    public void TypeRank_DecidesInsideOnePriorityBand()
    {
        var ranked = RankedCandidateOrdering.Order(new List<SearchResultMapper.RankedCandidate>
        {
            Candidate(@"C:\a-file.txt", typeRank: 1),
            Candidate(@"C:\z-app.lnk", typeRank: 0),
        }, new RankContext(Priorities()));

        Assert.AreEqual(@"C:\z-app.lnk", ranked[0].Result.FullPath);
    }

    [TestMethod]
    public void WithoutResolver_OrderIsUnchanged()
    {
        var ranked = RankedCandidateOrdering.Order(new List<SearchResultMapper.RankedCandidate>
        {
            Candidate(@"C:\b.txt"),
            Candidate(@"C:\a.txt"),
        });

        Assert.AreEqual(@"C:\a.txt", ranked[0].Result.FullPath);
    }

    [TestMethod]
    public void DefaultPriority_IsNormal()
    {
        var ranked = RankedCandidateOrdering.Order(new List<SearchResultMapper.RankedCandidate>
        {
            Candidate(@"C:\b.txt"),
            Candidate(@"C:\a.txt"),
        }, new RankContext(Priorities()));

        Assert.AreEqual(@"C:\a.txt", ranked[0].Result.FullPath);
    }

    private static SearchResultMapper.RankedCandidate HistoryRow(string path, string name) =>
        new(
            new AppSearchResult { FullPath = path, Name = name, ResultKind = string.Empty },
            false,
            5,
            0,
            new MatchRank(MatchRank.TierName, 0, 1.0, 1.0),
            path,
            IsHistoryOnly: true);

    // Both rows were opened five times, so both clear the H5 threshold; only the cap says how many get it.
    [TestMethod]
    public void InlineCaps_OnlyTheTopHistoryFiveRow_IsBoosted()
    {
        // The budget ranks the two 500-point rows by path, so aaa takes the single H5 slot even though its
        // name sorts last -- that is what puts it ahead of the H3 row below.
        var ranked = RankedCandidateOrdering.Order(
            [HistoryRow(@"D:\h\bbb.txt", "alpha"), HistoryRow(@"D:\h\aaa.txt", "zeta")],
            new RankContext(Priorities(), historyFiveMax: 1, historyThreeMax: 3));

        Assert.AreEqual(@"D:\h\aaa.txt", ranked[0].Result.FullPath);
        Assert.AreEqual(@"D:\h\bbb.txt", ranked[1].Result.FullPath);
    }

    [TestMethod]
    public void DefaultCaps_KeepBothHistoryFiveRowsBoosted()
    {
        // Same two rows, default 3/10: both are H5 at 34, so the score no longer separates them and the
        // shared name order decides -- the contrast that shows the inline cap demoted the second row.
        var ranked = RankedCandidateOrdering.Order(
            [HistoryRow(@"D:\h\bbb.txt", "alpha"), HistoryRow(@"D:\h\aaa.txt", "zeta")],
            new RankContext(Priorities()));

        Assert.AreEqual(@"D:\h\bbb.txt", ranked[0].Result.FullPath);
        Assert.AreEqual(@"D:\h\aaa.txt", ranked[1].Result.FullPath);
    }
    [TestMethod]
    public void AlwaysTopHighRule_PinsEvenAWeakRow()
    {
        // A pinned high rule reaches the Pinned section, which outranks every other section -- including a
        // normal row that matched perfectly, and a favorite.
        Func<string, bool, PriorityVerdict> priorities = (path, _) => path.Contains("high", StringComparison.OrdinalIgnoreCase)
            ? new PriorityVerdict(IndexRuleEffect.High, true)
            : new PriorityVerdict(IndexRuleEffect.Normal, false);

        var ranked = RankedCandidateOrdering.Order(new List<SearchResultMapper.RankedCandidate>
        {
            Candidate(@"C:\a-normal.txt", start: 0, coverage: 1.0),
            Candidate(@"C:\z-high.txt", start: 9, coverage: 0.2),
        }, new RankContext(priorities));

        Assert.AreEqual(@"C:\z-high.txt", ranked[0].Result.FullPath);
        Assert.AreEqual(@"C:\a-normal.txt", ranked[1].Result.FullPath);
    }
}
