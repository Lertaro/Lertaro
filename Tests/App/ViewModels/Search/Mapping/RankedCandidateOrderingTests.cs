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
    public void SecondaryAlias_DoesNotSpendTheHistorySlot()
    {
        // The alias is Grade 4 (curated alias / secondary field), so EffectiveHistoryTier can never lift it.
        // It must not take the single H5 slot from the Grade 1 direct file -- even though its path sorts
        // first and both carry the same history score.
        var aliasRank = new MatchRank(MatchRank.TierName, 0, 1.0, 1.0);
        var alias = new SearchResultMapper.RankedCandidate(
            new AppSearchResult { FullPath = @"D:\h\a.txt", Name = "电报", ResultKind = string.Empty },
            false,
            10,
            0,
            aliasRank,
            @"D:\h\a.txt",
            Evidence: MatchEvidence.Accepted(
                "telegram", "电报", MatchEvidenceField.CuratedAlias, aliasRank));
        var directRank = new MatchRank(MatchRank.TierName, 0, 1.0, 1.0);
        var direct = new SearchResultMapper.RankedCandidate(
            new AppSearchResult { FullPath = @"D:\h\b.txt", Name = "telegram.txt", ResultKind = string.Empty },
            false,
            10,
            0,
            directRank,
            @"D:\h\b.txt",
            Evidence: MatchEvidence.Accepted(
                "telegram", "telegram.txt", MatchEvidenceField.FileName, directRank));

        var ranked = RankedCandidateOrdering.OrderWithTrace(
            [alias, direct],
            new RankContext(Priorities(), historyFiveMax: 1, historyThreeMax: 3),
            out var trace);

        Assert.AreEqual(@"D:\h\b.txt", ranked[0].Result.FullPath);
        Assert.AreEqual(HistoryTier.H5, trace.DescribeAt(0, RankingProviderState.Unknown).Tier);
        Assert.AreEqual(HistoryTier.None, trace.DescribeAt(1, RankingProviderState.Unknown).Tier);
        Assert.IsTrue(direct.Result.IsFrequent);
        Assert.IsFalse(alias.Result.IsFrequent);
    }

    [TestMethod]
    public void DefaultCaps_KeepBothHistoryFiveRowsBoosted()
    {
        // Same two rows, default 3/10: both are H5, so the tier no longer separates them and the shared
        // name order decides -- the contrast that shows the inline cap demoted the second row.
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

    // The display cap keeps each type's best few rows: with 60 rows, the three rows of the last type sit
    // beyond the 50-row cut, and the reservation is what pulls them back in.
    [TestMethod]
    public void EnsureTypeCoverage_KeepsTheBestRowsOfATypePastTheCap()
    {
        var ordered = new List<SearchResultMapper.RankedCandidate>();
        for (var i = 0; i < 57; i++)
            ordered.Add(Candidate($@"C:\bulk\b{i:00}.txt", typeRank: 9));
        for (var i = 0; i < 3; i++)
            ordered.Add(Candidate($@"C:\tail\t{i}.txt", typeRank: 0));

        var kept = RankedCandidateOrdering.EnsureTypeCoverage(ordered, 50);

        Assert.AreEqual(50, kept.Count);
        Assert.IsTrue(kept.Exists(c => c.Result.FullPath == @"C:\tail\t0.txt"));
        Assert.IsTrue(kept.Exists(c => c.Result.FullPath == @"C:\tail\t2.txt"));
        // Only three slots go to the sparse type; the rest still follow the global order.
        Assert.AreEqual(3, kept.Count(c => c.TypeRank == 0));
        Assert.AreEqual(@"C:\bulk\b00.txt", kept[0].Result.FullPath);
    }

    // Selection never reorders: the rows that survive keep their relative order.
    [TestMethod]
    public void EnsureTypeCoverage_DoesNotReorderTheKeptRows()
    {
        var ordered = new List<SearchResultMapper.RankedCandidate>();
        for (var i = 0; i < 55; i++)
            ordered.Add(Candidate($@"C:\bulk\b{i:00}.txt", typeRank: 9));
        ordered.Add(Candidate(@"C:\tail\t.txt", typeRank: 0));

        var kept = RankedCandidateOrdering.EnsureTypeCoverage(ordered, 50);
        var expected = ordered.Where(c => kept.Contains(c)).Select(c => c.Result.FullPath).ToList();

        CollectionAssert.AreEqual(expected, kept.Select(c => c.Result.FullPath).ToList());
        Assert.IsTrue(kept.Exists(c => c.Result.FullPath == @"C:\tail\t.txt"));
    }

    // Array.Sort must keep each retained key paired with its row.
    [TestMethod]
    public void TraceKeepsEachRowsOwnSignalsAfterTheSort()
    {
        var context = new RankContext(Priorities());
        var ranked = RankedCandidateOrdering.OrderWithTrace(
            new List<SearchResultMapper.RankedCandidate>
            {
                Candidate(@"C:\a-normal.txt", coverage: 0.5),
                Candidate(@"C:\z-high.txt", coverage: 0.2),
            },
            context,
            out var trace);

        var first = trace.DescribeAt(0, RankingProviderState.Unknown);
        var second = trace.DescribeAt(1, RankingProviderState.Unknown);
        Assert.AreEqual(@"C:\z-high.txt", ranked[0].Result.FullPath);
        Assert.AreEqual(1, first.KernelRank);
        Assert.AreEqual(IndexRuleEffect.High, first.Priority);
        Assert.AreEqual(IndexRuleEffect.Normal, second.Priority);
        Assert.AreEqual(0.2, first.Match.Coverage, 0.0001);
        Assert.AreEqual(0.5, second.Match.Coverage, 0.0001);
    }

    [TestMethod]
    public void MergeCombinesDiagnosticSourcesWithoutChangingTheWinningRow()
    {
        var indexed = Candidate(@"C:\same.lnk") with { Source = RankingCandidateSource.FileIndex };
        var history = Candidate(@"C:\same.lnk") with
        {
            Source = RankingCandidateSource.History,
            IsHistoryOnly = true,
            HistoryCount = 5,
        };

        var ranked = RankedCandidateOrdering.OrderWithTrace([indexed, history], null, out var trace);
        var described = trace.DescribeAt(0, RankingProviderState.Unknown);

        Assert.HasCount(1, ranked);
        Assert.AreSame(indexed.Result, ranked[0].Result);
        Assert.AreEqual(RankingCandidateSource.FileIndex | RankingCandidateSource.History, described.Source);
        Assert.AreEqual(2, trace.PreMergeCount);
        Assert.AreEqual(1, trace.PostMergeCount);
    }

    [TestMethod]
    public void ContextDecidesWhetherAnExactDirectoryKeepsItsIdentity()
    {
        // Reported case: order "Files, Applications, System settings" and a query whose only settings hit
        // matched a keyword. With the cap on, the folder name is demoted to DirectStrong and that keyword
        // ExactIdentity wins; with the cap off (Files first) the folder's stronger match orders first.
        var folderMatch = new MatchRank(MatchRank.TierName, 0, 1, 1, MatchRank.ShapeWholeWord);
        var folder = Candidate(@"D:\VPN") with
        {
            Result = new AppSearchResult { FullPath = @"D:\VPN", Name = "VPN", IsDir = true },
            Match = folderMatch,
            Evidence = MatchEvidence.Accepted("vpn", "VPN", MatchEvidenceField.FileName, folderMatch),
        };
        var settingsMatch = new MatchRank(MatchRank.TierInitials, 1, 0.5, 0.5, MatchRank.ShapeScattered);
        var settings = Candidate(@"__builtin::Settings::ncpa.cpl", typeRank: 2) with
        {
            Result = new AppSearchResult { FullPath = @"__builtin::Settings::ncpa.cpl", Name = "查看网络连接" },
            Match = settingsMatch,
            Evidence = new MatchEvidence(
                MatchEvidenceField.Keyword,
                MatchEvidenceMode.Literal,
                MatchQueryVariant.Original,
                MatchEvidenceGrade.ExactIdentity,
                true,
                false),
        };

        var capped = RankedCandidateOrdering.Order([folder, settings], new RankContext(Priorities()));
        var filesFirst = RankedCandidateOrdering.Order(
            [folder, settings],
            new RankContext(Priorities(), demoteDirectoryIdentity: false));

        Assert.AreEqual("查看网络连接", capped[0].Result.Name);
        Assert.AreEqual("VPN", filesFirst[0].Result.Name);
    }

    [TestMethod]
    public void MergeKeepsApplicationExecutionObjectAndFilePathSignals()
    {
        const string path = @"C:\Apps\PixPin.lnk";
        var fileMatch = new MatchRank(MatchRank.TierName, 0, 1, 1, MatchRank.ShapeWholeWord);
        var file = Candidate(path) with
        {
            IsFavorite = true,
            HistoryCount = 7,
            Match = fileMatch,
            Source = RankingCandidateSource.FileIndex | RankingCandidateSource.Favorite,
            Evidence = MatchEvidence.Accepted("pixpin", "PixPin.lnk", MatchEvidenceField.FileName, fileMatch),
        };
        var appResult = new AppSearchResult
        {
            FullPath = path,
            Name = "PixPin",
            ResultKind = "Application",
            SourceProvider = new object(),
        };
        var appMatch = new MatchRank(
            MatchRank.TierName,
            4,
            0.5,
            0.5,
            MatchRank.ShapeScattered);
        var app = Candidate(path, typeRank: 0) with
        {
            Result = appResult,
            Match = appMatch,
            Source = RankingCandidateSource.SearchableProvider,
            Evidence = MatchEvidence.Accepted(
                "pixpin",
                "screen capture",
                MatchEvidenceField.CuratedAlias,
                appMatch),
        };
        var context = new RankContext((_, _) => new PriorityVerdict(IndexRuleEffect.Low, false));

        var ranked = RankedCandidateOrdering.OrderWithTrace([file, app], context, out var trace);
        var reversed = RankedCandidateOrdering.OrderWithTrace([app, file], context, out var reversedTrace);
        var described = trace.DescribeAt(0, RankingProviderState.ReadyWithItems);
        var reversedDescription = reversedTrace.DescribeAt(0, RankingProviderState.ReadyWithItems);

        Assert.HasCount(1, ranked);
        Assert.AreSame(appResult, ranked[0].Result);
        Assert.AreEqual(fileMatch, ranked[0].Match);
        Assert.IsTrue(ranked[0].IsFavorite);
        Assert.AreEqual(7, ranked[0].HistoryCount);
        Assert.AreSame(appResult, reversed[0].Result);
        Assert.AreEqual(fileMatch, reversed[0].Match);
        Assert.AreEqual(IndexRuleEffect.Low, described.Priority);
        Assert.AreEqual(MatchEvidenceField.FileName, described.Evidence.Field);
        Assert.AreEqual(described.Evidence, reversedDescription.Evidence);
        Assert.AreEqual(
            RankingCandidateSource.FileIndex |
            RankingCandidateSource.SearchableProvider |
            RankingCandidateSource.Favorite,
            described.Source);
    }

    [TestMethod]
    public void MergeDoesNotLendLearnedAliasQualityToDirectText()
    {
        const string path = @"C:\Apps\Editor.lnk";
        var directMatch = new MatchRank(
            MatchRank.TierName,
            3,
            0.5,
            0.5,
            MatchRank.ShapeScattered);
        var direct = Candidate(path) with
        {
            Match = directMatch,
            Source = RankingCandidateSource.SearchableProvider,
            Evidence = MatchEvidence.Accepted(
                "edit",
                "My Editor",
                MatchEvidenceField.DisplayTitle,
                directMatch),
        };
        var learnedMatch = new MatchRank(MatchRank.TierName, 0, 1, 1);
        var learned = Candidate(path) with
        {
            Match = learnedMatch,
            IsHistoryOnly = true,
            HistoryCount = 8,
            Source = RankingCandidateSource.History,
            Evidence = MatchEvidence.Accepted(
                "edit",
                "edit",
                MatchEvidenceField.HistoryKeyword,
                learnedMatch,
                MatchEvidenceMode.LearnedAlias),
        };

        var ranked = RankedCandidateOrdering.Order([learned, direct]);

        Assert.HasCount(1, ranked);
        Assert.AreEqual(directMatch, ranked[0].Match);
        Assert.AreEqual(MatchEvidenceField.DisplayTitle, ranked[0].Evidence.Field);
        Assert.IsFalse(ranked[0].IsHistoryOnly);
        Assert.AreEqual(8, ranked[0].HistoryCount);
    }

    [TestMethod]
    public void EvidenceGrade_OrdersDirectMatchesAheadOfSecondaryAliases()
    {
        var secondaryAlias = Candidate(@"C:\a.txt", coverage: 1) with
        {
            Evidence = MatchEvidence.Accepted("a", "a", MatchEvidenceField.CuratedAlias, new MatchRank(MatchRank.TierName, 0, 1)),
        };
        var directFile = Candidate(@"C:\b.txt", coverage: 0.4) with
        {
            Evidence = MatchEvidence.Accepted("a", "b.txt", MatchEvidenceField.FileName, new MatchRank(MatchRank.TierName, 0, 0.4)),
        };

        var ordered = RankedCandidateOrdering.Order([secondaryAlias, directFile]);

        CollectionAssert.AreEqual(
            new[] { @"C:\b.txt", @"C:\a.txt" },
            ordered.Select(row => row.NormalizedPath).ToArray());
    }

    [TestMethod]
    public void MergeRetainsApplicationIdentityAndStrongestEvidenceForDiagnostics()
    {
        const string path = @"C:\Apps\PixPin.lnk";
        var file = Candidate(path) with
        {
            Source = RankingCandidateSource.FileIndex,
            Evidence = MatchEvidence.Accepted("pixpin", "launcher", MatchEvidenceField.CuratedAlias,
                new MatchRank(MatchRank.TierName, 0, 1)),
        };
        var appResult = new AppSearchResult
        {
            FullPath = path,
            Name = "PixPin",
            ResultKind = "Application",
        };
        var app = Candidate(path) with
        {
            Result = appResult,
            Source = RankingCandidateSource.SearchableProvider,
            Evidence = MatchEvidence.Accepted("pixpin", "PixPin", MatchEvidenceField.DisplayTitle,
                new MatchRank(MatchRank.TierName, 0, 1)),
        };

        var ranked = RankedCandidateOrdering.OrderWithTrace([file, app], null, out var trace);
        var described = trace.DescribeAt(0, RankingProviderState.ReadyWithItems);

        Assert.AreSame(appResult, ranked[0].Result);
        Assert.AreEqual(MatchEvidenceGrade.ExactIdentity, described.Evidence.Grade);
        Assert.AreEqual(
            RankingCandidateKind.File | RankingCandidateKind.Application | RankingCandidateKind.Shortcut,
            described.Identity.Kind);
        Assert.AreEqual(
            RankingCandidateSource.FileIndex | RankingCandidateSource.SearchableProvider,
            described.Identity.Source);
    }

}
