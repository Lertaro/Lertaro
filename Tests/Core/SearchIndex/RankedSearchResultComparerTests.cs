using Lertaro.Core.IndexV2.Search;
using Lertaro.Core.SearchIndex;
using PriorityVerdict = Lertaro.Core.ExclusionRuleSet.PriorityVerdict;

namespace Lertaro.Core.Tests.SearchIndex;

// Full-window ordering over precomputed section, score, quality and path keys.
[TestClass]
public sealed class RankedSearchResultComparerTests
{
    private const ulong EngineTieBits = 1UL;

    private static ulong Key(MatchRank rank) =>
        (ulong)SearchResultRelevance.HighKey(rank) << 32 | EngineTieBits;

    private static readonly MatchRank Strong = new(MatchRank.TierName, 0, 1d, 1d, MatchRank.ShapeWholeWord);
    private static readonly MatchRank Weak = new(MatchRank.TierName, 0, 1d, 1d, MatchRank.ShapeScattered);

    private static SearchResult Row(
        string path,
        string? name = null,
        MatchRank? rank = null,
        bool isDir = false,
        string drive = "D",
        IndexRuleEffect effect = IndexRuleEffect.Normal,
        bool alwaysTop = false,
        bool favorite = false,
        int scoreX100 = 0,
        ulong? key = null)
    {
        var result = new SearchResult
        {
            Name = name ?? System.IO.Path.GetFileName(path),
            Path = path,
            IsDir = isDir,
            Drive = drive,
            RankSortKey = key ?? Key(rank ?? Weak),
        };

        var scores = scoreX100 == 0
            ? null
            : new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { [path] = scoreX100 };
        var favorites = favorite
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase) { path }
            : null;

        SearchResultRanking.Decorate(result, new RankContext(
            (_, _) => new PriorityVerdict(effect, alwaysTop),
            scores,
            favorites));
        return result;
    }

    private static List<string> Order(params SearchResult[] rows)
    {
        var list = rows.ToList();
        list.Sort(RankedSearchResultComparer.Instance);
        return list.Select(r => r.FullPath).ToList();
    }

    [TestMethod]
    public void Compare_NormalStrongBeforeNormalWeak() =>
        CollectionAssert.AreEqual(
            new[] { @"D:\dir\strong.txt", @"D:\dir\weak.txt" },
            Order(
                Row(@"D:\dir\weak.txt", rank: Weak),
                Row(@"D:\dir\strong.txt", rank: Strong)));

    [TestMethod]
    public void Compare_HighPriorityWeak_AfterNormalStrong()
    {
        // The 0.8 exchange rate again: a high bucket bought with a weak match (16) loses to a normal
        // bucket with a strong one (18).
        CollectionAssert.AreEqual(
            new[] { @"D:\dir\normal.txt", @"D:\dir\high.txt" },
            Order(
                Row(@"D:\dir\high.txt", rank: Weak, effect: IndexRuleEffect.High),
                Row(@"D:\dir\normal.txt", rank: Strong)));
    }

    [TestMethod]
    public void Compare_LowPriorityStrong_AfterNormalWeak() =>
        CollectionAssert.AreEqual(
            new[] { @"D:\dir\normal.txt", @"D:\dir\low.txt" },
            Order(
                Row(@"D:\dir\low.txt", rank: Strong, effect: IndexRuleEffect.Low),
                Row(@"D:\dir\normal.txt", rank: Weak)));

    [TestMethod]
    public void Compare_FavoriteStrong_LeadsHistoryH5() =>
        CollectionAssert.AreEqual(
            new[] { @"D:\dir\favorite.txt", @"D:\dir\often.txt" },
            Order(
                Row(@"D:\dir\often.txt", rank: Strong, scoreX100: 900),
                Row(@"D:\dir\favorite.txt", rank: Strong, favorite: true)));

    [TestMethod]
    public void Compare_HistoryH5_LeadsNormalStrong() =>
        CollectionAssert.AreEqual(
            new[] { @"D:\dir\often.txt", @"D:\dir\plain.txt" },
            Order(
                Row(@"D:\dir\plain.txt", rank: Strong),
                Row(@"D:\dir\often.txt", rank: Strong, scoreX100: 620)));

    [TestMethod]
    public void Compare_HistoryH3_BetweenH5AndNormalStrong() =>
        CollectionAssert.AreEqual(
            new[] { @"D:\dir\five.txt", @"D:\dir\three.txt", @"D:\dir\plain.txt" },
            Order(
                Row(@"D:\dir\plain.txt", rank: Strong),
                Row(@"D:\dir\three.txt", rank: Strong, scoreX100: 340),
                Row(@"D:\dir\five.txt", rank: Strong, scoreX100: 900)));

    [TestMethod]
    public void Compare_AlwaysTopWeak_LeadsEverything() =>
        CollectionAssert.AreEqual(
            new[] { @"D:\dir\pinned.txt", @"D:\dir\favorite.txt", @"D:\dir\five.txt", @"D:\dir\weak.txt" },
            Order(
                Row(@"D:\dir\weak.txt", rank: Weak),
                Row(@"D:\dir\five.txt", rank: Strong, scoreX100: 900),
                Row(@"D:\dir\favorite.txt", rank: Strong, favorite: true),
                Row(@"D:\dir\pinned.txt", rank: Weak, effect: IndexRuleEffect.High, alwaysTop: true)));

    [TestMethod]
    public void Compare_EqualScore_UsesPackedQuality()
    {
        // Both rows are strong normal matches at 18, so the packed key decides -- and every field of that
        // key is stored as "smaller is better", so the smaller high word is the better match.
        var atStart = Key(new MatchRank(MatchRank.TierName, 0, 1d, 1d, MatchRank.ShapeWholeWord));
        var lateInside = Key(new MatchRank(MatchRank.TierName, 9, 0.4d, 1d, MatchRank.ShapeInsideWord));

        CollectionAssert.AreEqual(
            new[] { @"D:\dir\at-start.txt", @"D:\dir\late-inside.txt" },
            Order(
                Row(@"D:\dir\late-inside.txt", key: lateInside),
                Row(@"D:\dir\at-start.txt", key: atStart)));
    }

    [TestMethod]
    public void Compare_CommonTail_FileBeforeFolder() =>
        CollectionAssert.AreEqual(
            new[] { @"D:\dir\same", @"D:\dir\other" },
            Order(
                Row(@"D:\dir\other", name: "same", isDir: true),
                Row(@"D:\dir\same", name: "same")));

    [TestMethod]
    public void Compare_CommonTail_NameThenLengthThenPathThenDriveThenPathLength()
    {
        // Every row here is a weak normal match at equal score, so only the shared tail can order them.
        // 1. natural name order: digits compare by value.
        CollectionAssert.AreEqual(
            new[] { @"D:\dir\second", @"D:\dir\tenth" },
            Order(Row(@"D:\dir\tenth", name: "a10"), Row(@"D:\dir\second", name: "a2")));

        // 2. names the natural order calls equal fall through to name length.
        CollectionAssert.AreEqual(
            new[] { @"D:\dir\p\a2", @"D:\dir\p\a02" },
            Order(Row(@"D:\dir\p\a02", name: "a02"), Row(@"D:\dir\p\a2", name: "a2")));

        // 3. same name, shorter path first.
        CollectionAssert.AreEqual(
            new[] { @"D:\p\a.txt", @"D:\pp\a.txt" },
            Order(Row(@"D:\pp\a.txt"), Row(@"D:\p\a.txt")));

        // 4. same-length paths and same name, the drive letter decides.
        CollectionAssert.AreEqual(
            new[] { @"C:\x\a.txt", @"D:\x\a.txt" },
            Order(Row(@"D:\x\a.txt", drive: "D"), Row(@"C:\x\a.txt", drive: "C")));

        // 5. same drive and same length, then the path itself, case-insensitively.
        CollectionAssert.AreEqual(
            new[] { @"D:\x\a.txt", @"D:\y\a.txt" },
            Order(Row(@"D:\y\a.txt"), Row(@"D:\x\a.txt")));
    }

    [TestMethod]
    public void Compare_EmptyAndNullAreSafe()
    {
        var row = Row(@"D:\dir\a.txt");

        Assert.AreEqual(0, RankedSearchResultComparer.Instance.Compare(row, row));
        Assert.IsLessThan(0, RankedSearchResultComparer.Instance.Compare(null, row));
        Assert.IsGreaterThan(0, RankedSearchResultComparer.Instance.Compare(row, null));
        Assert.AreEqual(0, RankedSearchResultComparer.Instance.Compare(null, null));
    }
}
