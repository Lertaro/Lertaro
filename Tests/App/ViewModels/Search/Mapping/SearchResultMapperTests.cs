using Lertaro.Core;
using Lertaro.Core.SearchIndex;
using Lertaro.App.ViewModels.Search.Mapping;
using PriorityVerdict = Lertaro.Core.ExclusionRuleSet.PriorityVerdict;

namespace Lertaro.App.Tests.ViewModels.Search.Mapping;

[TestClass]
public sealed class RankAndDedupeTests
{
    private static AppSearchResult Result(string path, bool isDir = false, string? resultKind = null) =>
        new() { FullPath = path, Name = path, IsDir = isDir, ResultKind = resultKind ?? string.Empty };

    // Bucket by the words in the path: "high" and "low" name their own bucket, everything else is normal.
    // Pinned adds AlwaysTop to the high rows, which is what reaches the Pinned section.
    private static Func<string, bool, PriorityVerdict> PrefixPriorities(bool pinnedHigh = false) =>
        (path, _) => path.Contains("high", StringComparison.OrdinalIgnoreCase)
            ? new PriorityVerdict(IndexRuleEffect.High, pinnedHigh)
            : path.Contains("low", StringComparison.OrdinalIgnoreCase)
                ? new PriorityVerdict(IndexRuleEffect.Low, false)
                : new PriorityVerdict(IndexRuleEffect.Normal, false);

    private static SearchResultMapper.RankedCandidate Candidate(
        string path,
        bool isFavorite = false,
        int historyCount = 0,
        int typeRank = int.MaxValue,
        int start = 0,
        double coverage = 1.0,
        double contiguity = 1.0,
        int tier = MatchRank.TierName,
        int shape = MatchRank.ShapeWholeWord,
        bool isDir = false,
        string? normalizedPath = null,
        bool isHistoryOnly = false,
        string? resultKind = null) =>
        new(Result(path, isDir, resultKind), isFavorite, historyCount, typeRank,
            new MatchRank(tier, start, coverage, contiguity, shape), normalizedPath ?? path, isHistoryOnly);

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
    public void RankAndDedupe_FavoriteStrongMatch_BeatsABetterQualityNonFavorite()
    {
        // A strong direct match puts a favorite in Pinned, not a score reward: a late start and low
        // coverage is still a whole-word literal hit, so it beats a better-matching ordinary Boosted row.
        var ranked = SearchResultMapper.RankAndDedupe(new List<SearchResultMapper.RankedCandidate>
        {
            Candidate(@"C:\favorite", isFavorite: true, start: 5, coverage: 0.3),
            Candidate(@"C:\plain", coverage: 1.0),
        });

        Assert.AreEqual(@"C:\favorite", ranked[0].FullPath);
    }

    [TestMethod]
    public void RankAndDedupe_FavoriteWeakMatch_KeepsTheBaseScore()
    {
        // The same favorite one notch down: a rare-reading hit is weak, so it stays in Boosted at
        // DirectApproximate and the better match wins.
        var ranked = SearchResultMapper.RankAndDedupe(new List<SearchResultMapper.RankedCandidate>
        {
            Candidate(@"C:\favorite", isFavorite: true, tier: MatchRank.TierFullRare),
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
    public void RankAndDedupe_HistoryH5_BeatsANormalStrongMatch()
    {
        // Neither row supplies MatchEvidence, so both resolve to DirectStrong; H5 then wins the
        // same-grade history tie-break.
        var ranked = SearchResultMapper.RankAndDedupe(new List<SearchResultMapper.RankedCandidate>
        {
            Candidate(@"C:\often", historyCount: 5, start: 5, coverage: 0.3),
            Candidate(@"C:\exact", historyCount: 0, coverage: 1.0),
        });

        Assert.AreEqual(@"C:\often", ranked[0].FullPath);
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
    public void RankAndDedupe_QualityBeatsTypeAtEqualGrade()
    {
        // Both rows are strong (whole-word, common reading), so they share a grade; match quality decides
        // before the display type order, which is only the final tie-break.
        var ranked = SearchResultMapper.RankAndDedupe(new List<SearchResultMapper.RankedCandidate>
        {
            Candidate(@"C:\files-perfect", typeRank: 2, start: 0, coverage: 1.0, contiguity: 1.0),
            Candidate(@"C:\apps-weak", typeRank: 1, start: 9, coverage: 0.2, contiguity: 0.4),
        });

        Assert.AreEqual(@"C:\files-perfect", ranked[0].FullPath);
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
    public void RankAndDedupe_FavoriteStrongMatch_BeatsTypeGroupOrder()
    {
        // The favorite enters Pinned, so it outranks the application row's Boosted section before any type
        // order is consulted. Type only breaks a complete quality tie, pinned by
        // RankedCandidateOrderingTests.TypeRank_DecidesInsideOnePriorityBand.
        var ranked = SearchResultMapper.RankAndDedupe(new List<SearchResultMapper.RankedCandidate>
        {
            Candidate(@"C:\app", typeRank: 0),
            Candidate(@"C:\favorite", isFavorite: true, typeRank: 3),
        });

        Assert.AreEqual(@"C:\favorite", ranked[0].FullPath);
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
    public void RankAndDedupe_ApplicationRowOwnsItsShortcutPath_EvenWhenFilesRankFirst()
    {
        const string shortcut = @"C:\Users\Linked\Desktop\屏幕标注截图录像ocr.lnk";

        var ranked = SearchResultMapper.RankAndDedupe(new List<SearchResultMapper.RankedCandidate>
        {
            Candidate(shortcut, typeRank: 0, resultKind: "File"),
            Candidate(shortcut, typeRank: 1, resultKind: "Application"),
        });

        Assert.HasCount(1, ranked);
        Assert.AreEqual("Application", ranked[0].ResultKind);
    }

    [TestMethod]
    public void RankAndDedupe_FileRowSurvivesWhenNoApplicationRowClaimsThePath()
    {
        var ranked = SearchResultMapper.RankAndDedupe(new List<SearchResultMapper.RankedCandidate>
        {
            Candidate(@"C:\plain.txt", typeRank: 0, resultKind: "File"),
            Candidate(@"C:\other.txt", typeRank: 0, resultKind: "File"),
        });

        Assert.HasCount(2, ranked);
    }

    [TestMethod]
    public void RankAndDedupe_HighPriorityWeak_RanksBelowNormalPriorityStrong()
    {
        // The 0.8 exchange rate: one priority step (8) buys less than one match step (10), so tier 3 lands
        // after tier 2 -- 16 under 18.
        var ranked = SearchResultMapper.RankAndDedupe(new List<SearchResultMapper.RankedCandidate>
        {
            Candidate(@"C:\high-weak.txt", tier: MatchRank.TierFullRare),
            Candidate(@"C:\normal-strong.txt"),
        }, new RankContext(PrefixPriorities()));

        Assert.AreEqual(@"C:\normal-strong.txt", ranked[0].FullPath);
        Assert.AreEqual(@"C:\high-weak.txt", ranked[1].FullPath);
    }

    [TestMethod]
    public void RankAndDedupe_PinnedHighPriorityWeak_LeadsAFavorite()
    {
        // AlwaysTop is a section, not a score: Pinned outranks every Boosted row, a favorite pin included.
        var ranked = SearchResultMapper.RankAndDedupe(new List<SearchResultMapper.RankedCandidate>
        {
            Candidate(@"C:\favorite", isFavorite: true),
            Candidate(@"C:\high-weak.txt", tier: MatchRank.TierFullRare),
        }, new RankContext(PrefixPriorities(pinnedHigh: true)));

        Assert.AreEqual(@"C:\high-weak.txt", ranked[0].FullPath);
        Assert.AreEqual(@"C:\favorite", ranked[1].FullPath);
    }

    [TestMethod]
    public void RankAndDedupe_LowPriorityStrong_RanksBelowNormalWeak()
    {
        // Low is a hard section, so its strong match (10) still sits under a weak normal row (8) in Boosted.
        var ranked = SearchResultMapper.RankAndDedupe(new List<SearchResultMapper.RankedCandidate>
        {
            Candidate(@"C:\low-strong.txt"),
            Candidate(@"C:\normal-weak.txt", tier: MatchRank.TierFullRare),
        }, new RankContext(PrefixPriorities()));

        Assert.AreEqual(@"C:\normal-weak.txt", ranked[0].FullPath);
        Assert.AreEqual(@"C:\low-strong.txt", ranked[1].FullPath);
    }

    [TestMethod]
    public void RankAndDedupe_TwoOpenHistoryTail_RanksBelowLowPriorityStrong()
    {
        // Tier 6 lands after tier 5: one or two weighted opens only reach the history tail, and the whole
        // low-priority bucket stays ahead of it.
        var ranked = SearchResultMapper.RankAndDedupe(new List<SearchResultMapper.RankedCandidate>
        {
            Candidate(@"C:\ghost-twice", historyCount: 2, isHistoryOnly: true),
            Candidate(@"C:\low-strong.txt"),
        }, new RankContext(PrefixPriorities()));

        Assert.AreEqual(@"C:\low-strong.txt", ranked[0].FullPath);
        Assert.AreEqual(@"C:\ghost-twice", ranked[1].FullPath);
    }

    [TestMethod]
    public void RankAndDedupe_FavoriteStrongMatch_BeatsHistoryH5()
    {
        var ranked = SearchResultMapper.RankAndDedupe(new List<SearchResultMapper.RankedCandidate>
        {
            Candidate(@"C:\ghost-many", historyCount: 5, isHistoryOnly: true),
            Candidate(@"C:\favorite", isFavorite: true),
        });

        Assert.AreEqual(@"C:\favorite", ranked[0].FullPath);
        Assert.AreEqual(@"C:\ghost-many", ranked[1].FullPath);
    }

    [TestMethod]
    public void RankAndDedupe_QualityDecidesWithinStrongGrades_AndTypeNeverLiftsWeakOverStrong()
    {
        // I2: within clean direct matches, match quality still comes before the type tie-break.
        var sameGrade = SearchResultMapper.RankAndDedupe(new List<SearchResultMapper.RankedCandidate>
        {
            Candidate(@"C:\files.txt", typeRank: 2),
            Candidate(@"C:\apps.txt", typeRank: 0, start: 3, coverage: 0.5),
        });
        Assert.AreEqual(@"C:\files.txt", sameGrade[0].FullPath);

        // I1: a rare/weak match of the leading type cannot jump across EvidenceGrade over a strong match.
        var acrossGrades = SearchResultMapper.RankAndDedupe(new List<SearchResultMapper.RankedCandidate>
        {
            Candidate(@"C:\files.txt", typeRank: 2),
            Candidate(@"C:\apps.txt", typeRank: 0, tier: MatchRank.TierFullRare),
        });
        Assert.AreEqual(@"C:\files.txt", acrossGrades[0].FullPath);
        Assert.AreEqual(@"C:\apps.txt", acrossGrades[1].FullPath);
    }

    [TestMethod]
    public void RankAndDedupe_QualityGroupsApproximateGrades()
    {
        // Both rows are DirectApproximate; the better-quality file row wins, and type only breaks a
        // complete quality tie.
        var ranked = SearchResultMapper.RankAndDedupe(new List<SearchResultMapper.RankedCandidate>
        {
            Candidate(@"C:\files.txt", typeRank: 2, start: 0, coverage: 1.0, contiguity: 1.0,
                shape: MatchRank.ShapeScattered),
            Candidate(@"C:\apps.lnk", typeRank: 0, start: 5, coverage: 0.2, contiguity: 0.2,
                shape: MatchRank.ShapeScattered),
        });

        Assert.AreEqual(@"C:\files.txt", ranked[0].FullPath);
        Assert.AreEqual(@"C:\apps.lnk", ranked[1].FullPath);
    }

    // Type only breaks a complete quality tie: a weaker grade of the leading type still loses to a strong
    // match further down.
    [TestMethod]
    public void RankAndDedupe_TypeRankDoesNotLiftABarelyMatchingRow()
    {
        var ranked = SearchResultMapper.RankAndDedupe(new List<SearchResultMapper.RankedCandidate>
        {
            Candidate(@"C:\files.txt", typeRank: 2),
            Candidate(@"C:\apps.txt", typeRank: 0, tier: MatchRank.TierFullRare, coverage: 0.2),
        });

        Assert.AreEqual(@"C:\files.txt", ranked[0].FullPath);
    }

    // The badge is driven by the same budget that decides the score: a row only counts as "frequent" once it
    // reaches H5 or H3. A pure-history row with one or two opens sits in the history tail and stays plain.
    [TestMethod]
    public void RankAndDedupe_MarksOnlyHistoryTierRowsAsFrequent()
    {
        var ranked = SearchResultMapper.RankAndDedupe(new List<SearchResultMapper.RankedCandidate>
        {
            Candidate(@"C:\h5", historyCount: 6),
            Candidate(@"C:\h3", historyCount: 4),
            Candidate(@"C:\tail", historyCount: 2, isHistoryOnly: true),
            Candidate(@"C:\plain"),
        });

        Assert.IsTrue(ranked.Single(r => r.FullPath == @"C:\h5").IsFrequent);
        Assert.IsTrue(ranked.Single(r => r.FullPath == @"C:\h3").IsFrequent);
        Assert.IsFalse(ranked.Single(r => r.FullPath == @"C:\tail").IsFrequent);
        Assert.IsFalse(ranked.Single(r => r.FullPath == @"C:\plain").IsFrequent);
    }

    [TestMethod]
    public void RankAndDedupe_EmptyInput_ReturnsEmptyList() =>
        Assert.IsEmpty(SearchResultMapper.RankAndDedupe(new List<SearchResultMapper.RankedCandidate>()));
}
