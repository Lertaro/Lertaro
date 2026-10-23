using Lertaro.Core.IndexV2.Search;
using Lertaro.Core.SearchIndex;
using PriorityVerdict = Lertaro.Core.ExclusionRuleSet.PriorityVerdict;

namespace Lertaro.Core.Tests.SearchIndex;

[TestClass]
public sealed class SearchResultRankingTests
{
    private const int SectionMask = 3;
    private const int StrongBit = 4;
    private const ulong EngineTieBits = 1UL;

    private static SearchResult Result(string path, ulong key = 0, bool isDir = false) =>
        new()
        {
            Name = System.IO.Path.GetFileName(path),
            Path = path,
            IsDir = isDir,
            Drive = path[..1],
            RankSortKey = key,
        };

    private static ulong PackRank(MatchRank rank) =>
        ((ulong)SearchResultRelevance.HighKey(rank) << 32) | EngineTieBits;

    private static ulong StrongKey() =>
        PackRank(new MatchRank(MatchRank.TierName, 0, 1d, 1d, MatchRank.ShapeWholeWord));

    private static ulong WeakKey() =>
        PackRank(new MatchRank(MatchRank.TierName, 0, 0.4d, 0.5d, MatchRank.ShapeScattered));

    private static Func<string, bool, PriorityVerdict> Rules(IndexRuleEffect effect, bool alwaysTop = false) =>
        (_, _) => new PriorityVerdict(effect, alwaysTop);

    private static RankContext Context(
        IndexRuleEffect effect = IndexRuleEffect.Normal,
        bool alwaysTop = false,
        int scoreX100 = 0,
        string scorePath = @"D:\dir\a.txt",
        string? favorite = null,
        string? query = null)
    {
        var scores = scoreX100 == 0
            ? null
            : new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { [scorePath] = scoreX100 };
        var favorites = favorite == null
            ? null
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase) { favorite };
        return new RankContext(Rules(effect, alwaysTop), scores, favorites, query: query);
    }

    [TestMethod]
    public void Decorate_ExactStemMatchWithQuery_AssignsExactIdentityGrade()
    {
        var exact = Result(@"D:\dir\editor.txt", PackRank(new MatchRank(MatchRank.TierName, 0, 0.6d, 1d, MatchRank.ShapeWordStart)));
        var partialHistory = Result(@"D:\dir\old-editor.txt", PackRank(new MatchRank(MatchRank.TierName, 4, 0.43d, 1d, MatchRank.ShapeWordStart)));

        var ctx = Context(scoreX100: 800, scorePath: @"D:\dir\old-editor.txt", query: "editor");
        SearchResultRanking.Decorate(exact, ctx);
        SearchResultRanking.Decorate(partialHistory, ctx);

        Assert.AreEqual(MatchEvidenceGrade.ExactIdentity, SearchResultRanking.GradeOf(exact));
        Assert.AreEqual(MatchEvidenceGrade.DirectStrong, SearchResultRanking.GradeOf(partialHistory));
        Assert.IsLessThan(0, RankedSearchResultComparer.Instance.Compare(exact, partialHistory));
    }

    [TestMethod]
    public void Decorate_ExactDirectory_IsDirectStrongNotExactIdentity()
    {
        var folder = Result(@"F:\Documents\PowerShell", StrongKey(), isDir: true);

        SearchResultRanking.Decorate(folder, Context(query: "powershell"));

        Assert.AreEqual(MatchEvidenceGrade.DirectStrong, SearchResultRanking.GradeOf(folder));
    }

    [TestMethod]
    public void Decorate_WholeStemReading_GradesSpacedQueryLikeTheQuickWindow()
    {
        // One reading run over the whole stem of a CJK name whose syllables the query happens to swap:
        // "sousuo jieguo" finds 搜索 and 结果 in the other order, so the quick window's joined branch
        // declines and leaves the row at Grade 2. The packed key cannot tell the two apart, so claiming a
        // joined witness here handed the full window's copy of the same row Grade 0.
        var reading = PackRank(new MatchRank(MatchRank.TierInitials, 0, 0.5d, 1d, MatchRank.ShapeWordStart));
        var spaced = Result(@"D:\dir\结果搜索.txt", reading);
        var joined = Result(@"D:\dir\结果搜索.txt", reading);

        SearchResultRanking.Decorate(spaced, Context(query: "sousuo jieguo"));
        SearchResultRanking.Decorate(joined, Context(query: "sousuojieguo"));

        Assert.AreEqual(MatchEvidenceGrade.DirectStrong, SearchResultRanking.GradeOf(spaced));
        Assert.AreEqual(MatchEvidenceGrade.ExactIdentity, SearchResultRanking.GradeOf(joined));
    }

    [TestMethod]
    public void Decorate_StrongLowPriority_StaysInLowSection()
    {
        var result = Result(@"D:\dir\a.txt", StrongKey());

        SearchResultRanking.Decorate(result, Context(IndexRuleEffect.Low, scoreX100: 700));

        Assert.AreEqual((byte)RankSection.Low, (byte)(result.RankFlags & SectionMask));
        Assert.AreEqual(StrongBit, result.RankFlags & StrongBit);
    }

    [TestMethod]
    public void Decorate_ZeroKey_IsApproximateWithoutHistoryTier()
    {
        var result = Result(@"D:\dir\a.txt", key: 0);

        SearchResultRanking.Decorate(result, Context(scoreX100: 900));

        Assert.AreEqual(0, result.RankFlags & StrongBit);
        Assert.AreEqual(MatchEvidenceGrade.DirectApproximate, SearchResultRanking.GradeOf(result));
        Assert.AreEqual((byte)HistoryTier.None, (byte)((result.RankFlags >> 3) & 3));
        Assert.AreEqual(900, result.HistoryScoreX100);
    }

    [TestMethod]
    public void Decorate_FavoriteStrongPins_WhileFavoriteWeakStaysInBoosted()
    {
        var favStrong = Result(@"D:\dir\a.txt", StrongKey());
        var favWeak = Result(@"D:\dir\a.txt", WeakKey());

        SearchResultRanking.Decorate(favStrong, Context(favorite: @"D:\dir\a.txt"));
        SearchResultRanking.Decorate(favWeak, Context(favorite: @"D:\dir\a.txt"));

        Assert.AreEqual((byte)RankSection.Pinned, (byte)(favStrong.RankFlags & SectionMask));
        Assert.AreEqual((byte)RankSection.Boosted, (byte)(favWeak.RankFlags & SectionMask));
    }

    [TestMethod]
    public void Decorate_AlwaysTopWeak_IsPinned()
    {
        var result = Result(@"D:\dir\a.txt", WeakKey());

        SearchResultRanking.Decorate(result, Context(IndexRuleEffect.High, alwaysTop: true));

        Assert.AreEqual((byte)RankSection.Pinned, (byte)(result.RankFlags & SectionMask));
    }

    [TestMethod]
    public void QuickAndFullWindows_PreservePairwiseOrderAcrossGradesPriorityAndHistory()
    {
        const string query = "editor";
        var specs = new (string Path, MatchRank Match, IndexRuleEffect Priority, bool AlwaysTop, bool Favorite, int HistoryX100)[]
        {
            (@"D:\Pinned\e-d-i-t-o-r.txt", new MatchRank(MatchRank.TierName, 0, 0.55d, 0.5d, MatchRank.ShapeScattered), IndexRuleEffect.High, true, false, 0),
            (@"D:\Fav\my-editor.txt", new MatchRank(MatchRank.TierName, 3, 0.46d, 1d, MatchRank.ShapeWordStart), IndexRuleEffect.Normal, false, true, 0),
            (@"D:\Normal\editor.txt", new MatchRank(MatchRank.TierName, 0, 0.6d, 1d, MatchRank.ShapeWordStart), IndexRuleEffect.Normal, false, false, 0),
            (@"D:\High\pro-editor.txt", new MatchRank(MatchRank.TierName, 4, 0.43d, 1d, MatchRank.ShapeWordStart), IndexRuleEffect.High, false, false, 600),
            (@"D:\Normal\team-editor.txt", new MatchRank(MatchRank.TierName, 5, 0.4d, 1d, MatchRank.ShapeWordStart), IndexRuleEffect.Normal, false, false, 350),
            (@"D:\High\e-d-i-t-o-r.txt", new MatchRank(MatchRank.TierName, 0, 0.55d, 0.5d, MatchRank.ShapeScattered), IndexRuleEffect.High, false, false, 700),
            (@"D:\Normal\ancestor-only.txt", new MatchRank(MatchRank.TierName, 12, 0.25d, 1d, MatchRank.ShapeNoName), IndexRuleEffect.Normal, false, false, 0),
            (@"D:\Low\editor.txt", new MatchRank(MatchRank.TierName, 0, 0.6d, 1d, MatchRank.ShapeWordStart), IndexRuleEffect.Low, false, false, 650),
        };

        var scores = specs.Where(s => s.HistoryX100 > 0)
            .ToDictionary(s => s.Path, s => s.HistoryX100, StringComparer.OrdinalIgnoreCase);
        var favorites = specs.Where(s => s.Favorite)
            .Select(s => s.Path)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var priorityMap = specs.ToDictionary(
            s => s.Path,
            s => new PriorityVerdict(s.Priority, s.AlwaysTop),
            StringComparer.OrdinalIgnoreCase);
        var ctx = new RankContext(
            (path, _) => priorityMap[path],
            scores,
            favorites,
            query: query);

        var fullRows = specs.Select(s =>
        {
            var r = Result(s.Path, PackRank(s.Match));
            SearchResultRanking.Decorate(r, ctx);
            return r;
        }).Reverse().ToList();
        fullRows.Sort(RankedSearchResultComparer.Instance);

        var quickSignals = specs.Select(s => (
            Signals: new RankSignals(
                s.Match,
                IsHistoryOnly: false,
                s.Favorite,
                s.Priority,
                s.AlwaysTop,
                s.HistoryX100,
                TypeRank: 0,
                IsDir: false,
                System.IO.Path.GetFileName(s.Path),
                s.Path,
                RankPolicy.DriveRank("D"),
                EngineTieBits),
            Evidence: s.Match.Shape == MatchRank.ShapeNoName
                ? MatchEvidence.LegacyUnknown
                : MatchEvidence.Accepted(query, System.IO.Path.GetFileName(s.Path), MatchEvidenceField.FileName, s.Match)))
            .ToArray();
        var budget = RankPolicy.BuildHistoryBudget(quickSignals.Select(q => q.Signals));
        var quickKeys = quickSignals
            .Select(q => RankPolicy.BuildKey(q.Signals, budget.GetValueOrDefault(q.Signals.NormalizedPath, HistoryTier.None), q.Evidence))
            .Reverse()
            .ToArray();
        Array.Sort(quickKeys, RankKeyComparer.Instance);

        CollectionAssert.AreEqual(
            quickKeys.Select(k => k.NormalizedPath).ToArray(),
            fullRows.Select(r => r.Path).ToArray());
    }

    [TestMethod]
    public void Decorate_IsIdempotent()
    {
        var context = Context(IndexRuleEffect.High, scoreX100: 620, query: "a");
        var once = Result(@"D:\dir\a.txt", StrongKey());
        SearchResultRanking.Decorate(once, context);

        var twice = Result(@"D:\dir\a.txt", StrongKey());
        SearchResultRanking.Decorate(twice, context);
        SearchResultRanking.Decorate(twice, context);

        Assert.AreEqual(once.RankScore, twice.RankScore);
        Assert.AreEqual(once.RankFlags, twice.RankFlags);
    }
}
