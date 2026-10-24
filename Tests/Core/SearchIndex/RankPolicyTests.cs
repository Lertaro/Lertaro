using Lertaro.Core.SearchIndex;

namespace Lertaro.Core.Tests.SearchIndex;

[TestClass]
public sealed class RankPolicyTests
{
    private static MatchRank Strong(
        int tier = MatchRank.TierName,
        int start = 0,
        double coverage = 1.0,
        double contiguity = 1.0,
        int shape = MatchRank.ShapeWholeWord) =>
        new(tier, start, coverage, contiguity, shape);

    private static MatchRank Weak() =>
        new(MatchRank.TierName, 0, 0.3, 0.4, MatchRank.ShapeScattered);

    private static RankSignals Signals(
        IndexRuleEffect priority = IndexRuleEffect.Normal,
        bool strong = true,
        bool alwaysTop = false,
        bool isFavorite = false,
        bool isHistoryOnly = false,
        int historyScoreX100 = 0,
        int typeRank = 0,
        bool isDir = false,
        string name = "item",
        string path = @"D:\item",
        int drive = 3,
        ulong engineTie = 0,
        MatchRank? match = null,
        bool typeRankApplies = true,
        bool demoteDirectoryIdentity = true) =>
        new(
            match ?? (strong ? Strong() : Weak()),
            isHistoryOnly,
            isFavorite,
            priority,
            alwaysTop,
            historyScoreX100,
            typeRank,
            isDir,
            name,
            path,
            drive,
            engineTie,
            typeRankApplies,
            demoteDirectoryIdentity);

    private static RankKey KeyOf(
        RankSignals s,
        HistoryTier tier = HistoryTier.None,
        MatchEvidence evidence = default) =>
        RankPolicy.BuildKey(s, tier, evidence);

    private static MatchEvidence EvidenceOf(MatchEvidenceGrade grade) =>
        new(
            grade is MatchEvidenceGrade.SecondaryField ? MatchEvidenceField.ShortcutTarget : MatchEvidenceField.FileName,
            MatchEvidenceMode.Literal,
            MatchQueryVariant.Original,
            grade,
            AllPositiveTerms: true,
            DirectText: grade <= MatchEvidenceGrade.DirectApproximate);

    [TestMethod]
    public void PolicyVersion_IsThree() =>
        Assert.AreEqual(3, CurrentVersion);

    // RankPolicy.Version is a const, so comparing it to a literal folds to AreEqual(3, 3) and trips
    // MSTEST0032. Reading it through a property keeps the guard able to fail when the version is bumped.
    private static int CurrentVersion => RankPolicy.Version;

    [TestMethod]
    public void Section_PinnedRequiresHighAndAlwaysTop_OrStrongFavorite()
    {
        Assert.AreEqual(RankSection.Pinned, RankPolicy.Section(Signals(IndexRuleEffect.High, alwaysTop: true, strong: false), HistoryTier.None));
        Assert.AreEqual(RankSection.Pinned, RankPolicy.Section(Signals(isFavorite: true, strong: true), HistoryTier.None));
        Assert.AreEqual(RankSection.Boosted, RankPolicy.Section(Signals(isFavorite: true, strong: false), HistoryTier.None));
        Assert.AreEqual(RankSection.Low, RankPolicy.Section(Signals(IndexRuleEffect.Low, isFavorite: true, strong: false), HistoryTier.None));
    }

    [TestMethod]
    public void Section_LowStaysLowEvenWithHighOpenCount_AndAllHistoryOnlyRowsEnterHistoryTail()
    {
        Assert.AreEqual(RankSection.Low, RankPolicy.Section(Signals(IndexRuleEffect.Low, historyScoreX100: 900), HistoryTier.H5));
        Assert.AreEqual(RankSection.HistoryTail, RankPolicy.Section(Signals(isHistoryOnly: true, historyScoreX100: 900), HistoryTier.H5));
        Assert.AreEqual(RankSection.HistoryTail, RankPolicy.Section(Signals(isHistoryOnly: true, historyScoreX100: 150), HistoryTier.Tail));
    }

    [TestMethod]
    public void Compare_V3FullLadder_RespectsSectionsAndMonotonicGrades()
    {
        var keys = new[]
        {
            KeyOf(Signals(isHistoryOnly: true, historyScoreX100: 800, name: "learned", path: @"D:\Learned\archive.bin"), HistoryTier.H5),
            KeyOf(Signals(IndexRuleEffect.Low, strong: false, name: "low-weak", path: @"D:\Low\misc.txt")),
            KeyOf(Signals(IndexRuleEffect.Low, name: "low-exact", path: @"D:\Low\report.pdf"), HistoryTier.H5, EvidenceOf(MatchEvidenceGrade.ExactIdentity)),
            KeyOf(Signals(IndexRuleEffect.High, typeRank: 0, name: "sec", path: @"D:\High\target-match.lnk"), HistoryTier.None, EvidenceOf(MatchEvidenceGrade.SecondaryField)),
            KeyOf(Signals(IndexRuleEffect.High, strong: false, typeRank: 0, name: "approx-high", path: @"D:\High\r-e-p-o-r-t.txt")),
            KeyOf(Signals(isFavorite: true, strong: false, typeRank: 2, name: "approx-fav", path: @"D:\Fav\r-e-p-o-r-t.txt")),
            KeyOf(Signals(typeRank: 1, name: "strong-normal", path: @"D:\Normal\report-final.docx")),
            KeyOf(Signals(IndexRuleEffect.High, typeRank: 1, historyScoreX100: 600, name: "strong-high", path: @"D:\High\report-old.docx"), HistoryTier.H5),
            KeyOf(Signals(typeRank: 1, name: "exact-file", path: @"D:\Normal\report.docx"), HistoryTier.None, EvidenceOf(MatchEvidenceGrade.ExactIdentity)),
            KeyOf(Signals(typeRank: 0, name: "exact-app", path: @"D:\Apps\report.lnk"), HistoryTier.None, EvidenceOf(MatchEvidenceGrade.ExactIdentity)),
            KeyOf(Signals(isFavorite: true, typeRank: 3, name: "fav-strong", path: @"D:\Fav\report.docx")),
            KeyOf(Signals(IndexRuleEffect.High, alwaysTop: true, strong: false, name: "pinned", path: @"D:\Pinned\r-e-p-o-r-t.txt")),
        };

        Array.Sort(keys, RankKeyComparer.Instance);

        CollectionAssert.AreEqual(
            new[]
            {
                @"D:\Pinned\r-e-p-o-r-t.txt",
                @"D:\Fav\report.docx",
                @"D:\Apps\report.lnk",
                @"D:\Normal\report.docx",
                @"D:\High\report-old.docx",
                @"D:\Normal\report-final.docx",
                @"D:\Fav\r-e-p-o-r-t.txt",
                @"D:\High\r-e-p-o-r-t.txt",
                @"D:\High\target-match.lnk",
                @"D:\Low\report.pdf",
                @"D:\Low\misc.txt",
                @"D:\Learned\archive.bin",
            },
            keys.Select(k => k.NormalizedPath).ToArray());
    }

    [TestMethod]
    public void Compare_BoostedQualityBeatsTypeRankOnEveryGrade()
    {
        var preferredStrongType = KeyOf(Signals(
            typeRank: 0,
            match: Strong(shape: MatchRank.ShapeInsideWord, coverage: 0.2, start: 5)));
        var betterQualityLowerType = KeyOf(Signals(typeRank: 1, match: Strong()));
        Assert.IsGreaterThan(0, RankKeyComparer.Instance.Compare(preferredStrongType, betterQualityLowerType));

        // Same DirectApproximate grade: the better-quality type 1 row wins, so type only breaks a
        // complete quality tie instead of grouping approximate matches by type.
        var approximatePreferredType = KeyOf(Signals(
            typeRank: 0,
            strong: false,
            match: new MatchRank(MatchRank.TierName, 0, 0.2, 0.4, MatchRank.ShapeScattered)));
        var approximateBetterQualityLowerType = KeyOf(Signals(
            typeRank: 1,
            strong: false,
            match: new MatchRank(MatchRank.TierName, 0, 1.0, 1.0, MatchRank.ShapeScattered)));
        Assert.IsGreaterThan(0, RankKeyComparer.Instance.Compare(approximatePreferredType, approximateBetterQualityLowerType));
    }

    [TestMethod]
    public void Compare_ExactDirectoryLosesToStrongApplicationOnConfiguredTypeOrder()
    {
        var folder = KeyOf(
            Signals(typeRank: 2, isDir: true, name: "PowerShell", path: @"F:\Documents\PowerShell"),
            HistoryTier.None,
            EvidenceOf(MatchEvidenceGrade.ExactIdentity));
        var app = KeyOf(
            Signals(typeRank: 0, name: "PowerShell 7 (x64)", path: @"C:\Start\PowerShell 7 (x64).lnk"),
            HistoryTier.None,
            EvidenceOf(MatchEvidenceGrade.DirectStrong));

        Assert.AreEqual(MatchEvidenceGrade.DirectStrong, folder.Grade);
        Assert.IsLessThan(0, RankKeyComparer.Instance.Compare(app, folder));
    }

    [TestMethod]
    public void Compare_ExactDirectoryKeepsItsIdentityWhenTheCapIsOff()
    {
        // The cap exists only to protect a type the user ranked ABOVE Files. With Files first there is
        // nothing to protect, so the exact folder name must stand as ExactIdentity -- keeping it at
        // DirectStrong let a settings row that only matched a keyword outrank the very bucket ranked first.
        var evidence = EvidenceOf(MatchEvidenceGrade.ExactIdentity);
        var capped = KeyOf(
            Signals(typeRank: 0, isDir: true, name: "VPN", path: @"D:\VPN"),
            HistoryTier.None,
            evidence);
        var uncapped = KeyOf(
            Signals(typeRank: 0, isDir: true, name: "VPN", path: @"D:\VPN", demoteDirectoryIdentity: false),
            HistoryTier.None,
            evidence);

        Assert.AreEqual(MatchEvidenceGrade.DirectStrong, capped.Grade);
        Assert.AreEqual(MatchEvidenceGrade.ExactIdentity, uncapped.Grade);
    }

    [TestMethod]
    public void Compare_LowQualityBeatsTypeRank()
    {
        var preferredType = KeyOf(Signals(
            IndexRuleEffect.Low, typeRank: 0, match: Strong(shape: MatchRank.ShapeInsideWord, coverage: 0.2, start: 5)));
        var betterQualityLowerType = KeyOf(Signals(
            IndexRuleEffect.Low, typeRank: 5, match: Strong(coverage: 1, start: 0)));

        // Low is still a section, not a free-for-all: match quality decides before the type tie-break.
        Assert.IsGreaterThan(0, RankKeyComparer.Instance.Compare(preferredType, betterQualityLowerType));
    }

    [TestMethod]
    public void Compare_TypeRankIsNeutralForUnlistedTypes()
    {
        var listedType = KeyOf(Signals(typeRank: 0, name: "z-listed", path: @"D:\z-listed"));
        var unlistedType = KeyOf(Signals(typeRank: 9, typeRankApplies: false, name: "a-unlisted", path: @"D:\a-unlisted"));

        // If the unlisted rank participated, type 0 would win; neutral means the common tail decides.
        Assert.IsGreaterThan(0, RankKeyComparer.Instance.Compare(listedType, unlistedType));
    }

    [TestMethod]
    public void Compare_HistoryTailKeepsRecencyBeforeType()
    {
        var preferredType = KeyOf(
            Signals(isHistoryOnly: true, typeRank: 0, historyScoreX100: 100, name: "older", path: @"D:\h\older"),
            HistoryTier.H3);
        var recentLowerType = KeyOf(
            Signals(isHistoryOnly: true, typeRank: 1, historyScoreX100: 900, name: "recent", path: @"D:\h\recent"),
            HistoryTier.H3);

        // HistoryTail is recency-first; the configured type order is only the final tie-break.
        Assert.IsGreaterThan(0, RankKeyComparer.Instance.Compare(preferredType, recentLowerType));
    }

    [TestMethod]
    public void Compare_HistoryTailComesAfterLowEvenAtH5()
    {
        var weakLow = KeyOf(Signals(IndexRuleEffect.Low, strong: false));
        var historyH5 = KeyOf(
            Signals(isHistoryOnly: true, historyScoreX100: 750), HistoryTier.H5);

        Assert.AreEqual((byte)RankSection.Low, weakLow.Section);
        Assert.AreEqual((byte)RankSection.HistoryTail, historyH5.Section);
        Assert.IsLessThan(0, RankKeyComparer.Instance.Compare(weakLow, historyH5));
    }

    [TestMethod]
    public void Compare_CommonTail_NameThenLengthThenPathThenDrive()
    {
        RankKey Key(string name, string path, int drive = 3, bool isDir = false) =>
            KeyOf(Signals(
                IndexRuleEffect.Low, strong: false, name: name, path: path, drive: drive, isDir: isDir));

        Assert.IsLessThan(0, RankKeyComparer.Instance.Compare(Key("a2", @"D:\x\a2"), Key("a10", @"D:\x\a10")));
        Assert.IsLessThan(0, RankKeyComparer.Instance.Compare(Key("a2", @"D:\x\a2"), Key("a02", @"D:\x\a02")));
        Assert.IsLessThan(
            0,
            RankKeyComparer.Instance.Compare(Key("zeta", @"D:\x\zeta"), Key("alpha", @"D:\x\alpha", isDir: true)));
        Assert.IsLessThan(0, RankKeyComparer.Instance.Compare(Key("same", @"D:\x\same"), Key("same", @"D:\yy\same")));
        Assert.IsLessThan(
            0,
            RankKeyComparer.Instance.Compare(Key("same", @"F:\x\same", drive: 1), Key("same", @"E:\x\same", drive: 2)));
        Assert.IsLessThan(
            0,
            RankKeyComparer.Instance.Compare(Key("same", @"D:\x\a\same"), Key("same", @"D:\x\b\same")));
        Assert.IsLessThan(0, RankKeyComparer.Instance.Compare(Key("same", @"D:\x\Same"), Key("same", @"D:\x\same")));
    }

    [TestMethod]
    public void Compare_EngineTieIsLastResort()
    {
        RankKey Key(ulong engineTie, string name = "same", string path = @"D:\x\same") =>
            KeyOf(Signals(IndexRuleEffect.Low, strong: false, name: name, path: path, engineTie: engineTie));

        Assert.IsLessThan(0, RankKeyComparer.Instance.Compare(Key(1), Key(2)));
        Assert.AreEqual(0, RankKeyComparer.Instance.Compare(Key(1), Key(1)));
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
            Assert.AreEqual(0, comparer.Compare(x, x));

        for (var i = 0; i < keys.Count; i++)
        {
            for (var j = 0; j < keys.Count; j++)
            {
                var forward = Math.Sign(comparer.Compare(keys[i], keys[j]));
                var backward = Math.Sign(comparer.Compare(keys[j], keys[i]));
                Assert.AreEqual(-forward, backward, $"pair {i},{j} is not antisymmetric");

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
                        Assert.IsLessThan(0, comparer.Compare(keys[i], keys[k]), $"triple {i},{j},{k} is not transitive");
                }
            }
        }
    }
}
