using Lertaro.Core.SearchIndex;

namespace Lertaro.Core.Tests.SearchIndex;

[TestClass]
public sealed class RankPolicyTests
{
    // Whole-word, left-aligned, full coverage: the best quality a strong match can carry.
    private static MatchRank Strong(
        int shape = MatchRank.ShapeWholeWord,
        int tier = MatchRank.TierName,
        double coverage = 1,
        double contiguity = 1,
        int start = 0) => new(tier, start, coverage, contiguity, shape);

    // Scattered with half the matched characters in separate runs: weak on every key.
    private static MatchRank Weak(int shape = MatchRank.ShapeScattered)
        => new(MatchRank.TierName, 0, 0.3, 0.5, shape);

    private static RankSignals Signals(
        IndexRuleEffect priority = IndexRuleEffect.Normal,
        bool strong = true,
        bool isHistoryOnly = false,
        bool isFavorite = false,
        bool alwaysTop = false,
        int historyScoreX100 = 0,
        int typeRank = 0,
        bool isDir = false,
        string name = "report",
        string path = @"D:\work\report.docx",
        int drive = 3,
        ulong engineTie = 0,
        MatchRank? match = null)
    {
        var quality = match ?? (strong ? Strong() : Weak());
        return new RankSignals(
            quality, isHistoryOnly, isFavorite, priority, alwaysTop,
            historyScoreX100, typeRank, isDir, name, path, drive, engineTie);
    }

    private static RankKey KeyOf(RankSignals signals, HistoryTier tier = HistoryTier.None)
        => RankPolicy.BuildKey(signals, tier);

    [TestMethod]
    public void Score_HighStrong_Is26() =>
        Assert.AreEqual(26, RankPolicy.Score(Signals(IndexRuleEffect.High), HistoryTier.None));

    [TestMethod]
    public void Score_NormalStrong_Is18() =>
        Assert.AreEqual(18, RankPolicy.Score(Signals(), HistoryTier.None));

    [TestMethod]
    public void Score_HighWeak_Is16() =>
        Assert.AreEqual(16, RankPolicy.Score(Signals(IndexRuleEffect.High, strong: false), HistoryTier.None));

    [TestMethod]
    public void Score_NormalWeak_Is8() =>
        Assert.AreEqual(8, RankPolicy.Score(Signals(strong: false), HistoryTier.None));

    [TestMethod]
    public void Score_LowStrong_Is10() =>
        Assert.AreEqual(10, RankPolicy.Score(Signals(IndexRuleEffect.Low), HistoryTier.None));

    [TestMethod]
    public void Score_LowWeak_Is0() =>
        Assert.AreEqual(0, RankPolicy.Score(Signals(IndexRuleEffect.Low, strong: false), HistoryTier.None));

    [TestMethod]
    public void Score_HighWeak_IsBelowNormalStrong() =>
        // Tier 3 lands after tier 2: one priority step buys 0.8 of a match step.
        Assert.IsLessThan(
            RankPolicy.Score(Signals(), HistoryTier.None),
            RankPolicy.Score(Signals(IndexRuleEffect.High, strong: false), HistoryTier.None));

    [TestMethod]
    public void Score_FavoriteStrong_Is44_AndFavoriteWeakKeepsBaseScore()
    {
        Assert.AreEqual(
            RankPolicy.FavoriteStrongScore,
            RankPolicy.Score(Signals(isFavorite: true), HistoryTier.None));
        Assert.AreEqual(
            8,
            RankPolicy.Score(Signals(strong: false, isFavorite: true), HistoryTier.None));
        Assert.AreEqual(
            16,
            RankPolicy.Score(Signals(IndexRuleEffect.High, strong: false, isFavorite: true), HistoryTier.None));
    }

    [TestMethod]
    public void Score_HistoryFiveThreeAndPureTail_Are34_22_6()
    {
        Assert.AreEqual(34, RankPolicy.Score(Signals(IndexRuleEffect.High), HistoryTier.H5));
        Assert.AreEqual(22, RankPolicy.Score(Signals(historyScoreX100: 320), HistoryTier.H3));
        Assert.AreEqual(
            RankPolicy.HistoryTailScore,
            RankPolicy.Score(Signals(isHistoryOnly: true, historyScoreX100: 250), HistoryTier.Tail));

        // One or two opens do not lift an indexed row: a non-history row with a Tail tier still scores C/P.
        Assert.AreEqual(18, RankPolicy.Score(Signals(), HistoryTier.Tail));
        Assert.AreEqual(0, RankPolicy.Score(Signals(IndexRuleEffect.Low, strong: false), HistoryTier.Tail));
    }

    [TestMethod]
    public void Section_AlwaysTopPinsOnlyHighPriority()
    {
        Assert.AreEqual(
            RankSection.Pinned,
            RankPolicy.Section(Signals(IndexRuleEffect.High, alwaysTop: true), HistoryTier.None));
        Assert.AreEqual(
            RankSection.Boosted,
            RankPolicy.Section(Signals(alwaysTop: true), HistoryTier.None));
        Assert.AreEqual(
            RankSection.Low,
            RankPolicy.Section(Signals(IndexRuleEffect.Low, alwaysTop: true), HistoryTier.None));
    }

    [TestMethod]
    public void Section_HistoryBucketsAndFavoriteStrongAreBoosted()
    {
        Assert.AreEqual(RankSection.Boosted, RankPolicy.Section(Signals(), HistoryTier.H5));
        Assert.AreEqual(RankSection.Boosted, RankPolicy.Section(Signals(), HistoryTier.H3));
        Assert.AreEqual(RankSection.Boosted, RankPolicy.Section(Signals(isFavorite: true), HistoryTier.None));
        // A lifted bucket outranks its own priority bucket: low priority plus H5 is still Boosted.
        Assert.AreEqual(
            RankSection.Boosted,
            RankPolicy.Section(Signals(IndexRuleEffect.Low, historyScoreX100: 600), HistoryTier.H5));
        // A favorite that only matched fuzzily keeps its priority bucket instead.
        Assert.AreEqual(
            RankSection.Low,
            RankPolicy.Section(Signals(IndexRuleEffect.Low, strong: false, isFavorite: true), HistoryTier.None));
    }

    [TestMethod]
    public void Section_PureHistoryTailIsNotLow()
    {
        Assert.AreEqual(
            RankSection.HistoryTail,
            RankPolicy.Section(Signals(isHistoryOnly: true, historyScoreX100: 250), HistoryTier.Tail));
        Assert.AreEqual(
            RankSection.HistoryTail,
            RankPolicy.Section(
                Signals(IndexRuleEffect.Low, isHistoryOnly: true, historyScoreX100: 250), HistoryTier.Tail));
    }

    [TestMethod]
    public void IsStrong_WholeWordWordStartInsideAndFullTier_AreStrong()
    {
        Assert.IsTrue(Strong().IsStrong);
        Assert.IsTrue(Strong(shape: MatchRank.ShapeWordStart).IsStrong);
        Assert.IsTrue(Strong(shape: MatchRank.ShapeInsideWord).IsStrong);
        Assert.IsTrue(Strong(tier: MatchRank.TierInitials).IsStrong);
        Assert.IsTrue(Strong(tier: MatchRank.TierFull).IsStrong);
    }

    [TestMethod]
    public void IsStrong_ScatteredNoNameNoPositiveAndRare_AreWeak()
    {
        Assert.IsFalse(Weak().IsStrong);
        Assert.IsFalse(Weak(shape: MatchRank.ShapeNoName).IsStrong);
        Assert.IsFalse(Strong(tier: MatchRank.TierFullRare).IsStrong);
        Assert.IsFalse(MatchRank.NoPositiveTerms.IsStrong);
        Assert.IsFalse(MatchRank.NoMatch.IsStrong);
    }

    [TestMethod]
    public void IsStrong_MultiTermContiguityBelowOne_StillStrong() =>
        Assert.IsTrue(new MatchRank(MatchRank.TierName, 0, 0.6, 0.5, MatchRank.ShapeWordStart).IsStrong);

    [TestMethod]
    public void BuildKey_AlwaysTopByteOnlySetForHigh()
    {
        Assert.AreEqual(1, KeyOf(Signals(IndexRuleEffect.High, alwaysTop: true)).AlwaysTop);
        Assert.AreEqual(0, KeyOf(Signals(alwaysTop: true)).AlwaysTop);
        Assert.AreEqual(0, KeyOf(Signals(IndexRuleEffect.Low, alwaysTop: true)).AlwaysTop);
        Assert.AreEqual(0, KeyOf(Signals(IndexRuleEffect.High)).AlwaysTop);

        Assert.AreEqual((byte)RankSection.Pinned, KeyOf(Signals(IndexRuleEffect.High, alwaysTop: true)).Section);
        Assert.AreEqual((byte)RankSection.Low, KeyOf(Signals(IndexRuleEffect.Low, alwaysTop: true)).Section);
    }

    [TestMethod]
    public void BuildKey_HistoryTieZeroWithoutHistory()
    {
        Assert.AreEqual(0, KeyOf(Signals(historyScoreX100: 600)).HistoryTie);
        Assert.AreEqual(600, KeyOf(Signals(historyScoreX100: 600), HistoryTier.H5).HistoryTie);
        Assert.AreEqual(250, KeyOf(Signals(isHistoryOnly: true, historyScoreX100: 250), HistoryTier.Tail).HistoryTie);
    }

    [TestMethod]
    public void HistoryTie_IndexedTailRow_IsZero()
    {
        // An indexed row keeps its C/P score at Tail, so it must not carry a history tie-break either.
        var key = KeyOf(Signals(historyScoreX100: 250), HistoryTier.Tail);

        Assert.AreEqual(0, key.HistoryTie);
        Assert.AreEqual(18, key.Score);
    }

    [TestMethod]
    public void HistoryTie_HistoryOnlyTailRow_KeepsScore()
    {
        var key = KeyOf(Signals(isHistoryOnly: true, historyScoreX100: 250), HistoryTier.Tail);

        Assert.AreEqual(250, key.HistoryTie);
        Assert.AreEqual((byte)RankSection.HistoryTail, key.Section);
        Assert.AreEqual(RankPolicy.HistoryTailScore, key.Score);
    }

    [TestMethod]
    public void Compare_IndexedTailRows_DoNotUseHistoryTie()
    {
        var preferredType = KeyOf(Signals(typeRank: 0, historyScoreX100: 100), HistoryTier.Tail);
        var moreHistory = KeyOf(Signals(typeRank: 5, historyScoreX100: 900), HistoryTier.Tail);

        Assert.AreEqual(RankSection.Boosted, RankPolicy.Section(Signals(), HistoryTier.Tail));
        Assert.AreEqual(0, preferredType.HistoryTie);
        Assert.AreEqual(0, moreHistory.HistoryTie);
        Assert.IsLessThan(0, RankKeyComparer.Instance.Compare(preferredType, moreHistory));
    }

    [TestMethod]
    public void Compare_FullLadderMatchesDocumentedExample()
    {
        // Feed the documented ten-row ladder in reverse order.
        var ladder = new[]
        {
            Signals(IndexRuleEffect.Low, strong: false, name: "misc.txt", path: @"D:\Low\misc.txt"),
            Signals(IndexRuleEffect.Low, name: "report.pdf", path: @"D:\Low\report.pdf"),
            Signals(strong: false, name: "reprot.txt", path: @"D:\Normal\reprot.txt"),
            Signals(IndexRuleEffect.High, strong: false, name: "r-e-p-o-r-t.txt", path: @"D:\High\r-e-p-o-r-t.txt"),
            Signals(name: "report-final.docx", path: @"D:\Normal\report-final.docx"),
            Signals(historyScoreX100: 320, name: "report.docx", path: @"D:\Normal\report.docx"),
            Signals(IndexRuleEffect.High, name: "report-old.docx", path: @"D:\High\report-old.docx"),
            Signals(IndexRuleEffect.High, historyScoreX100: 600, name: "report.docx", path: @"D:\High\report.docx"),
            Signals(isFavorite: true, name: "report.docx", path: @"D:\Fav\report.docx"),
            Signals(IndexRuleEffect.High, strong: false, alwaysTop: true,
                name: "r-e-p-o-r-t.txt", path: @"D:\Pinned\r-e-p-o-r-t.txt"),
        };
        var tiers = new[]
        {
            HistoryTier.None, HistoryTier.None, HistoryTier.None, HistoryTier.None, HistoryTier.None,
            HistoryTier.H3, HistoryTier.None, HistoryTier.H5, HistoryTier.None, HistoryTier.None,
        };

        var keys = new RankKey[ladder.Length];
        for (var i = 0; i < ladder.Length; i++)
            keys[i] = KeyOf(ladder[i], tiers[i]);

        Array.Sort(keys, RankKeyComparer.Instance);

        CollectionAssert.AreEqual(
            new[]
            {
                @"D:\Pinned\r-e-p-o-r-t.txt",
                @"D:\Fav\report.docx",
                @"D:\High\report.docx",
                @"D:\High\report-old.docx",
                @"D:\Normal\report.docx",
                @"D:\Normal\report-final.docx",
                @"D:\High\r-e-p-o-r-t.txt",
                @"D:\Normal\reprot.txt",
                @"D:\Low\report.pdf",
                @"D:\Low\misc.txt",
            },
            keys.Select(k => k.NormalizedPath).ToArray());
    }

    [TestMethod]
    public void Compare_BoostedTypeRankBeatsQuality()
    {
        var preferredType = KeyOf(Signals(
            typeRank: 0,
            match: Strong(shape: MatchRank.ShapeInsideWord, coverage: 0.2, start: 5)));
        var betterQuality = KeyOf(Signals(typeRank: 1, match: Strong()));

        Assert.IsLessThan(0, RankKeyComparer.Instance.Compare(preferredType, betterQuality));
    }

    [TestMethod]
    public void Compare_LowQualityBeatsTypeRank()
    {
        var betterQuality = KeyOf(Signals(
            IndexRuleEffect.Low, typeRank: 5, match: Strong(coverage: 1, start: 0)));
        var preferredType = KeyOf(Signals(
            IndexRuleEffect.Low, typeRank: 0, match: Strong(shape: MatchRank.ShapeInsideWord, coverage: 0.2, start: 5)));

        Assert.IsLessThan(0, RankKeyComparer.Instance.Compare(betterQuality, preferredType));
    }

    [TestMethod]
    public void Compare_HistoryTailComesAfterLow()
    {
        var weakLow = KeyOf(Signals(IndexRuleEffect.Low, strong: false));
        var historyTail = KeyOf(
            Signals(isHistoryOnly: true, historyScoreX100: 250), HistoryTier.Tail);

        Assert.AreEqual((byte)RankSection.Low, weakLow.Section);
        Assert.AreEqual((byte)RankSection.HistoryTail, historyTail.Section);
        Assert.AreEqual(6, historyTail.Score);
        Assert.IsLessThan(0, RankKeyComparer.Instance.Compare(weakLow, historyTail));
    }

    [TestMethod]
    public void Compare_CommonTail_NameThenLengthThenPathThenDrive()
    {
        // Every key above the tail is identical: same section, score, history tie, type rank and quality.
        RankKey Key(string name, string path, int drive = 3, bool isDir = false) =>
            KeyOf(Signals(
                IndexRuleEffect.Low, strong: false, name: name, path: path, drive: drive, isDir: isDir));

        // Natural name order: digits compare by value, so a2 precedes a10.
        Assert.IsLessThan(0, RankKeyComparer.Instance.Compare(Key("a2", @"D:\x\a2"), Key("a10", @"D:\x\a10")));
        // Names the natural order calls equal fall through to name length.
        Assert.IsLessThan(0, RankKeyComparer.Instance.Compare(Key("a2", @"D:\x\a2"), Key("a02", @"D:\x\a02")));
        // Files before folders, whatever the names say.
        Assert.IsLessThan(
            0,
            RankKeyComparer.Instance.Compare(Key("zeta", @"D:\x\zeta"), Key("alpha", @"D:\x\alpha", isDir: true)));
        // Same name, shorter path first.
        Assert.IsLessThan(0, RankKeyComparer.Instance.Compare(Key("same", @"D:\x\same"), Key("same", @"D:\yy\same")));
        // The drive key precedes the path key: a lower drive wins even when its path sorts later.
        Assert.IsLessThan(
            0,
            RankKeyComparer.Instance.Compare(Key("same", @"F:\x\same", drive: 1), Key("same", @"E:\x\same", drive: 2)));
        // Equal drives: the case-insensitive path key decides a difference beyond case.
        Assert.IsLessThan(
            0,
            RankKeyComparer.Instance.Compare(Key("same", @"D:\x\a\same"), Key("same", @"D:\x\b\same")));
        // Case-only differences are equal to OrdinalIgnoreCase, so the ordinal pass makes them strict.
        Assert.IsLessThan(0, RankKeyComparer.Instance.Compare(Key("same", @"D:\x\Same"), Key("same", @"D:\x\same")));
    }

    [TestMethod]
    public void Compare_EngineTieIsLastResort()
    {
        RankKey Key(ulong engineTie, string name = "same", string path = @"D:\x\same") =>
            KeyOf(Signals(IndexRuleEffect.Low, strong: false, name: name, path: path, engineTie: engineTie));

        Assert.IsLessThan(0, RankKeyComparer.Instance.Compare(Key(1), Key(2)));
        Assert.AreEqual(0, RankKeyComparer.Instance.Compare(Key(1), Key(1)));
        // The engine bits never overrule an earlier key.
        Assert.IsLessThan(0, RankKeyComparer.Instance.Compare(Key(9, "a2", @"D:\x\a2"), Key(1, "a10", @"D:\x\a10")));
    }

    [TestMethod]
    public void Compare_IsAntisymmetricAndTransitiveOverTheFixedLadder()
    {
        var keys = new List<RankKey>
        {
            KeyOf(Signals(IndexRuleEffect.High, alwaysTop: true, strong: false, name: "p1", path: @"D:\p\p1")),
            KeyOf(Signals(IndexRuleEffect.High, alwaysTop: true, name: "p2", path: @"D:\p\p2")),
            KeyOf(Signals(isFavorite: true, name: "f1", path: @"D:\f\f1")),
            KeyOf(Signals(isFavorite: true, strong: false, name: "f2", path: @"D:\f\f2")),
            KeyOf(Signals(IndexRuleEffect.High, historyScoreX100: 600, name: "h1", path: @"D:\h\h1"), HistoryTier.H5),
            KeyOf(Signals(historyScoreX100: 590, name: "h2", path: @"D:\h\h2"), HistoryTier.H5),
            KeyOf(Signals(historyScoreX100: 510, name: "h3", path: @"D:\h\h3"), HistoryTier.H5),
            KeyOf(Signals(IndexRuleEffect.Low, historyScoreX100: 420, name: "h4", path: @"D:\h\h4"), HistoryTier.H3),
            KeyOf(Signals(historyScoreX100: 300, name: "h5", path: @"D:\h\h5"), HistoryTier.H3),
            KeyOf(Signals(historyScoreX100: 250, isHistoryOnly: true, name: "t1", path: @"D:\t\t1"), HistoryTier.Tail),
            KeyOf(Signals(historyScoreX100: 100, isHistoryOnly: true, name: "t2", path: @"D:\t\t2"), HistoryTier.Tail),
            KeyOf(Signals(IndexRuleEffect.High, typeRank: 1, name: "a1", path: @"D:\a\a1")),
            KeyOf(Signals(IndexRuleEffect.High, typeRank: 0, name: "a2", path: @"D:\a\a2")),
            KeyOf(Signals(IndexRuleEffect.High, strong: false, name: "a3", path: @"D:\a\a3")),
            KeyOf(Signals(name: "a4", path: @"D:\a\a4", isDir: true)),
            KeyOf(Signals(name: "a5", path: @"D:\a\a5")),
            KeyOf(Signals(strong: false, name: "a6", path: @"D:\a\a6")),
            KeyOf(Signals(IndexRuleEffect.Low, name: "l1", path: @"D:\l\l1")),
            KeyOf(Signals(IndexRuleEffect.Low, strong: false, name: "l2", path: @"D:\l\l2")),
            KeyOf(Signals(IndexRuleEffect.Low, strong: false, name: "l3", path: @"D:\l\l3", drive: 4)),
        };
        var comparer = RankKeyComparer.Instance;

        foreach (var x in keys)
        {
            Assert.AreEqual(0, comparer.Compare(x, x));
        }

        for (var i = 0; i < keys.Count; i++)
        {
            for (var j = 0; j < keys.Count; j++)
            {
                var forward = Math.Sign(comparer.Compare(keys[i], keys[j]));
                var backward = Math.Sign(comparer.Compare(keys[j], keys[i]));
                Assert.AreEqual(-forward, backward, $"pair {i},{j} is not antisymmetric");

                // Every path in the list is distinct, so nothing but identity may compare equal.
                if (i != j)
                    Assert.AreNotEqual(0, forward, $"pair {i},{j} ties on distinct paths");
            }
        }

        for (var i = 0; i < keys.Count; i++)
        {
            for (var j = 0; j < keys.Count; j++)
            {
                for (var k = 0; k < keys.Count; k++)
                {
                    if (comparer.Compare(keys[i], keys[j]) < 0 && comparer.Compare(keys[j], keys[k]) < 0)
                    {
                        Assert.IsLessThan(0, comparer.Compare(keys[i], keys[k]), $"triple {i},{j},{k} is not transitive");
                    }
                }
            }
        }
    }

    [TestMethod]
    public void TypeBonus_DecaysWithTheUsersOrderAndUnlistedTypesGetNothing()
    {
        var match = Strong();

        Assert.AreEqual(16, RankPolicy.TypeBonus(0, match));
        Assert.AreEqual(8, RankPolicy.TypeBonus(1, match));
        Assert.AreEqual(4, RankPolicy.TypeBonus(2, match));
        Assert.AreEqual(1, RankPolicy.TypeBonus(3, match));
        Assert.AreEqual(0, RankPolicy.TypeBonus(4, match));
        Assert.AreEqual(0, RankPolicy.TypeBonus(int.MaxValue, match));
    }

    [TestMethod]
    public void TypeBonus_ScalesWithTheWeakMatchWeight()
    {
        var weak = new MatchRank(MatchRank.TierName, 0, 0.25, 1.0, MatchRank.ShapeScattered);

        Assert.AreEqual(4, RankPolicy.TypeBonus(0, weak));
        Assert.AreEqual(0, RankPolicy.TypeBonus(int.MaxValue, weak));
        Assert.AreEqual(0, RankPolicy.TypeBonus(0, MatchRank.NoMatch));
    }

    [TestMethod]
    public void QuickScore_WeightsOnlyTheBaseScore()
    {
        var strong = Signals(typeRank: 0);
        Assert.AreEqual(34, RankPolicy.QuickScore(strong, HistoryTier.None));
        Assert.AreEqual(18, RankPolicy.Score(strong, HistoryTier.None));

        var favorite = Signals(isFavorite: true, typeRank: 0);
        Assert.AreEqual(44, RankPolicy.QuickScore(favorite, HistoryTier.None));

        var h5 = Signals(typeRank: 0, historyScoreX100: 600);
        Assert.AreEqual(34, RankPolicy.QuickScore(h5, HistoryTier.H5));

        var tail = Signals(isHistoryOnly: true, strong: false, typeRank: 0, historyScoreX100: 200);
        Assert.AreEqual(6, RankPolicy.QuickScore(tail, HistoryTier.Tail));
    }
}
