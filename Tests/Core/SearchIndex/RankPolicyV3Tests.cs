using Lertaro.Core.IndexV2.Search;
using Lertaro.Core.SearchIndex;

namespace Lertaro.Core.Tests.SearchIndex;

[TestClass]
public sealed class RankPolicyV3Tests
{
    private static MatchRank Strong(
        int tier = MatchRank.TierName,
        int start = 0,
        double coverage = 1.0,
        double contiguity = 1.0,
        int shape = MatchRank.ShapeWholeWord) =>
        new(tier, start, coverage, contiguity, shape);

    private static MatchRank Weak(
        int tier = MatchRank.TierName,
        int start = 0,
        double coverage = 0.4,
        double contiguity = 0.5,
        int shape = MatchRank.ShapeScattered) =>
        new(tier, start, coverage, contiguity, shape);

    private static RankSignals Signals(
        string name,
        string path,
        MatchRank match,
        IndexRuleEffect priority = IndexRuleEffect.Normal,
        bool alwaysTop = false,
        bool isFavorite = false,
        bool isHistoryOnly = false,
        int historyScoreX100 = 0,
        int typeRank = 0,
        bool isDir = false,
        int drive = 3,
        ulong engineTie = 1) =>
        new(match, isHistoryOnly, isFavorite, priority, alwaysTop, historyScoreX100, typeRank, isDir, name, path, drive, engineTie);

    private static MatchEvidence Grade(MatchEvidenceGrade grade, bool direct = true) =>
        new(
            direct ? MatchEvidenceField.FileName : MatchEvidenceField.ShortcutTarget,
            MatchEvidenceMode.Literal,
            MatchQueryVariant.Original,
            grade,
            AllPositiveTerms: true,
            DirectText: direct);

    [TestMethod]
    public void Monotonicity_OrdinaryPreferencesNeverCrossEvidenceGrade()
    {
        var exactLowTypeNormal = RankPolicyV3.BuildKey(
            Signals("editor", @"D:\Normal\editor.txt", Strong(), typeRank: 3),
            HistoryTier.None,
            Grade(MatchEvidenceGrade.ExactIdentity));

        var strongTopTypeHighH5 = RankPolicyV3.BuildKey(
            Signals("old-editor.lnk", @"D:\High\old-editor.lnk", Strong(start: 4, coverage: 0.6, shape: MatchRank.ShapeWordStart),
                priority: IndexRuleEffect.High, historyScoreX100: 900, typeRank: 0),
            HistoryTier.H5,
            Grade(MatchEvidenceGrade.DirectStrong));

        var approxTopTypeHigh = RankPolicyV3.BuildKey(
            Signals("e-d-i-t-o-r.lnk", @"D:\High\e-d-i-t-o-r.lnk", Weak(),
                priority: IndexRuleEffect.High, historyScoreX100: 900, typeRank: 0),
            HistoryTier.None,
            Grade(MatchEvidenceGrade.DirectApproximate));

        var secondaryTopTypeHigh = RankPolicyV3.BuildKey(
            Signals(" launcher.lnk", @"D:\High\launcher.lnk", Strong(),
                priority: IndexRuleEffect.High, typeRank: 0),
            HistoryTier.None,
            Grade(MatchEvidenceGrade.SecondaryField, direct: false));

        var learnedManyOpens = RankPolicyV3.BuildKey(
            Signals("archive.bin", @"D:\High\archive.bin", MatchRank.NoMatch,
                priority: IndexRuleEffect.High, isHistoryOnly: true, historyScoreX100: 950, typeRank: 0),
            HistoryTier.H5,
            Grade(MatchEvidenceGrade.LearnedAlias, direct: false));

        var ordered = new[] { learnedManyOpens, secondaryTopTypeHigh, approxTopTypeHigh, strongTopTypeHighH5, exactLowTypeNormal };
        Array.Sort(ordered, V3RankKeyComparer.Instance);

        CollectionAssert.AreEqual(
            new[]
            {
                @"D:\Normal\editor.txt",
                @"D:\High\old-editor.lnk",
                @"D:\High\e-d-i-t-o-r.lnk",
                @"D:\High\launcher.lnk",
                @"D:\High\archive.bin",
            },
            ordered.Select(k => k.NormalizedPath).ToArray());
    }

    [TestMethod]
    public void ExplicitControls_AlwaysTopPinsAboveAll_WeakFavoriteStaysInOwnGrade_LowDemotesBelowNormal()
    {
        var pinnedWeak = RankPolicyV3.BuildKey(
            Signals("pinned.txt", @"D:\Pinned\pinned.txt", Weak(), IndexRuleEffect.High, alwaysTop: true),
            HistoryTier.None,
            Grade(MatchEvidenceGrade.DirectApproximate));

        var favoriteStrong = RankPolicyV3.BuildKey(
            Signals("fav-strong.txt", @"D:\Fav\fav-strong.txt", Strong(coverage: 0.5, start: 2), isFavorite: true, typeRank: 3),
            HistoryTier.None,
            Grade(MatchEvidenceGrade.DirectStrong));

        var plainExact = RankPolicyV3.BuildKey(
            Signals("exact.lnk", @"D:\Apps\exact.lnk", Strong(), typeRank: 0),
            HistoryTier.None,
            Grade(MatchEvidenceGrade.ExactIdentity));

        var favoriteWeak = RankPolicyV3.BuildKey(
            Signals("fav-weak.txt", @"D:\Fav\fav-weak.txt", Weak(), isFavorite: true, typeRank: 0),
            HistoryTier.None,
            Grade(MatchEvidenceGrade.DirectApproximate));

        var plainWeak = RankPolicyV3.BuildKey(
            Signals("plain-weak.txt", @"D:\Docs\plain-weak.txt", Weak(), typeRank: 0),
            HistoryTier.None,
            Grade(MatchEvidenceGrade.DirectApproximate));

        var lowExact = RankPolicyV3.BuildKey(
            Signals("low-exact.lnk", @"D:\Low\low-exact.lnk", Strong(), IndexRuleEffect.Low, historyScoreX100: 800, typeRank: 0),
            HistoryTier.H5,
            Grade(MatchEvidenceGrade.ExactIdentity));

        var ordered = new[] { lowExact, plainWeak, favoriteWeak, plainExact, favoriteStrong, pinnedWeak };
        Array.Sort(ordered, V3RankKeyComparer.Instance);

        CollectionAssert.AreEqual(
            new[]
            {
                @"D:\Pinned\pinned.txt",
                @"D:\Fav\fav-strong.txt",
                @"D:\Apps\exact.lnk",
                @"D:\Fav\fav-weak.txt",
                @"D:\Docs\plain-weak.txt",
                @"D:\Low\low-exact.lnk",
            },
            ordered.Select(k => k.NormalizedPath).ToArray());
    }

    [TestMethod]
    public void TotalOrder_IsAntisymmetricIrreflexiveTransitiveAndOrderIndependent()
    {
        var ladder = new[]
        {
            RankPolicyV3.BuildKey(Signals("p1", @"D:\p\p1", Weak(), IndexRuleEffect.High, alwaysTop: true), HistoryTier.None, Grade(MatchEvidenceGrade.DirectApproximate)),
            RankPolicyV3.BuildKey(Signals("f1", @"D:\f\f1", Strong(), isFavorite: true, typeRank: 2), HistoryTier.None, Grade(MatchEvidenceGrade.DirectStrong)),
            RankPolicyV3.BuildKey(Signals("e0", @"D:\e\e0", Strong(), typeRank: 0), HistoryTier.None, Grade(MatchEvidenceGrade.ExactIdentity)),
            RankPolicyV3.BuildKey(Signals("e1", @"D:\e\e1", Strong(), typeRank: 1), HistoryTier.H5, Grade(MatchEvidenceGrade.ExactIdentity)),
            RankPolicyV3.BuildKey(Signals("s0", @"D:\s\s0", Strong(start: 2, coverage: 0.6), IndexRuleEffect.High, typeRank: 0, historyScoreX100: 600), HistoryTier.H5, Grade(MatchEvidenceGrade.DirectStrong)),
            RankPolicyV3.BuildKey(Signals("s1", @"D:\s\s1", Strong(start: 2, coverage: 0.6), typeRank: 0, historyScoreX100: 350), HistoryTier.H3, Grade(MatchEvidenceGrade.DirectStrong)),
            RankPolicyV3.BuildKey(Signals("a-fav", @"D:\a\a-fav", Weak(), isFavorite: true, typeRank: 0), HistoryTier.None, Grade(MatchEvidenceGrade.DirectApproximate)),
            RankPolicyV3.BuildKey(Signals("a-high", @"D:\a\a-high", Weak(), IndexRuleEffect.High, typeRank: 2), HistoryTier.None, Grade(MatchEvidenceGrade.DirectApproximate)),
            RankPolicyV3.BuildKey(Signals("sec", @"D:\sec\sec", Strong(), typeRank: 0), HistoryTier.None, Grade(MatchEvidenceGrade.SecondaryField, direct: false)),
            RankPolicyV3.BuildKey(Signals("low", @"D:\low\low", Strong(), IndexRuleEffect.Low, typeRank: 0), HistoryTier.None, Grade(MatchEvidenceGrade.ExactIdentity)),
            RankPolicyV3.BuildKey(Signals("h5", @"D:\hist\h5", MatchRank.NoMatch, isHistoryOnly: true, historyScoreX100: 700), HistoryTier.H5, Grade(MatchEvidenceGrade.LearnedAlias, direct: false)),
            RankPolicyV3.BuildKey(Signals("tail", @"D:\hist\tail", MatchRank.NoMatch, isHistoryOnly: true, historyScoreX100: 150), HistoryTier.Tail, Grade(MatchEvidenceGrade.LearnedAlias, direct: false)),
        };

        var cmp = V3RankKeyComparer.Instance;
        for (var i = 0; i < ladder.Length; i++)
        {
            Assert.AreEqual(0, cmp.Compare(ladder[i], ladder[i]));
            for (var j = 0; j < ladder.Length; j++)
            {
                var ab = Math.Sign(cmp.Compare(ladder[i], ladder[j]));
                var ba = Math.Sign(cmp.Compare(ladder[j], ladder[i]));
                Assert.AreEqual(-ab, ba, $"Antisymmetry failed for ({i}, {j})");
                if (i != j)
                    Assert.AreNotEqual(0, ab, $"Distinct paths tied at ({i}, {j})");
                for (var k = 0; k < ladder.Length; k++)
                {
                    if (cmp.Compare(ladder[i], ladder[j]) < 0 && cmp.Compare(ladder[j], ladder[k]) < 0)
                        Assert.IsLessThan(0, cmp.Compare(ladder[i], ladder[k]), $"Transitivity failed for ({i}, {j}, {k})");
                }
            }
        }

        var forward = ladder.ToArray();
        var reversed = ladder.Reverse().ToArray();
        Array.Sort(forward, cmp);
        Array.Sort(reversed, cmp);
        CollectionAssert.AreEqual(
            forward.Select(k => k.NormalizedPath).ToArray(),
            reversed.Select(k => k.NormalizedPath).ToArray());
    }
}
