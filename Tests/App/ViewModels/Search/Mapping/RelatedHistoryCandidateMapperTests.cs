using Lertaro.App.ViewModels.Search.Mapping;
using Lertaro.Core;
using Lertaro.Core.SearchIndex;
using Lertaro.PluginSdk.Services;

namespace Lertaro.App.Tests.ViewModels.Search.Mapping;

[TestClass]
public sealed class RelatedHistoryCandidateMapperTests
{
    private static HistoryEntry Entry(string keyword, string path, int count = 3, long time = 0) =>
        new(keyword, path, HistoryEntryKind.File, time, count);

    [TestMethod]
    public void FindsContainedAndExtendedKeywordsButSkipsExactAssociations()
    {
        var entries = new[]
        {
            Entry("xcc", @"C:\Docs\许纯纯.txt"),
            Entry("xcc-chun", @"C:\Docs\许纯纯.txt"),
            Entry("cc", @"C:\Docs\Exact.txt"),
            Entry("other", @"C:\Docs\Other.txt"),
        };

        var matches = RelatedHistoryCandidateMapper.MatchRelated("cc", entries, maxMatches: 8);

        CollectionAssert.AreEqual(
            new[] { "xcc", "xcc-chun" },
            matches.Select(match => match.Keyword).ToList());
    }

    [TestMethod]
    public void PrefixRelationIsOrderedBeforeContainedRelation()
    {
        var entries = new[]
        {
            Entry("xcc", @"C:\Docs\Contained.txt", count: 20),
            Entry("cc-full", @"C:\Docs\Prefix.txt", count: 3),
        };

        var matches = RelatedHistoryCandidateMapper.MatchRelated("cc", entries, maxMatches: 8);

        Assert.AreEqual("cc-full", matches[0].Keyword);
        Assert.AreEqual("xcc", matches[1].Keyword);
    }

    [TestMethod]
    public void QueryExtensionRecallsTheShorterStoredKeyword()
    {
        var entries = new[] { Entry("app", @"C:\Docs\apple.txt") };

        var matches = RelatedHistoryCandidateMapper.MatchRelated(
            "apple", entries, maxMatches: 16);

        Assert.AreEqual("app", Assert.ContainsSingle(matches).Keyword);
    }

    [TestMethod]
    public void SingleCharacterQueriesDoNotUseWeakRecall()
    {
        var entries = new[] { Entry("xcc", @"C:\Docs\许纯纯.txt") };
        Assert.IsEmpty(RelatedHistoryCandidateMapper.MatchRelated("c", entries, maxMatches: 8));
    }

    [TestMethod]
    public void CollectCapsWeakRowsAtTwoAndHonoursLowPriority()
    {
        var entries = new[]
        {
            Entry("cca", @"C:\Docs\A.txt"),
            Entry("ccb", @"C:\Docs\Low.txt"),
            Entry("ccc", @"C:\Docs\C.txt"),
        };
        var context = new RankContext((path, _) =>
            path.Contains("Low", StringComparison.OrdinalIgnoreCase)
                ? new Core.ExclusionRuleSet.PriorityVerdict(IndexRuleEffect.Low, false)
                : new Core.ExclusionRuleSet.PriorityVerdict(IndexRuleEffect.Normal, false));

        var candidates = RelatedHistoryCandidateMapper.Collect(
            FuzzyQuery.Parse("cc"), scope: null, entries,
            _ => true, _ => false, context);

        Assert.HasCount(2, candidates);
        Assert.IsFalse(candidates.Any(candidate => candidate.NormalizedPath.Contains("Low")));
        Assert.IsTrue(candidates.All(candidate => candidate.IsHistoryOnly));
    }

    [TestMethod]
    public void NameMatchDoesNotTurnRelatedHistoryIntoADirectHit()
    {
        var entries = new[]
        {
            Entry("cca", @"C:\Docs\cc.txt"),
        };

        var candidates = RelatedHistoryCandidateMapper.Collect(
            FuzzyQuery.Parse("cc"), scope: null, entries,
            _ => true, _ => false);

        Assert.HasCount(1, candidates);
        Assert.IsTrue(candidates[0].IsHistoryOnly);
    }

    [TestMethod]
    public void FileFilterScopeExcludesRelatedHistoryOutsideItsFolders()
    {
        var entries = new[]
        {
            Entry("cca", @"C:\Allowed\cc.txt"),
            Entry("ccb", @"C:\Outside\cc.txt"),
        };

        var candidates = RelatedHistoryCandidateMapper.Collect(
            FuzzyQuery.Parse("cc"), scope: null, entries,
            _ => true, _ => false,
            pathFilter: path => path.StartsWith(@"C:\Allowed\", StringComparison.OrdinalIgnoreCase));

        Assert.HasCount(1, candidates);
        Assert.AreEqual(@"C:\Allowed\cc.txt", candidates[0].Result.FullPath);
    }

    [TestMethod]
    public void ScopeFilteringHappensBeforeTheTwoCandidateLimit()
    {
        var entries = Enumerable.Range(0, 16)
            .Select(i => Entry("cc-out", $@"C:\Outside\{i}.txt", time: 100 - i))
            .Append(Entry("cc-in", @"C:\Allowed\cc.txt", time: 1))
            .ToArray();

        var candidates = RelatedHistoryCandidateMapper.Collect(
            FuzzyQuery.Parse("cc"), scope: null, entries,
            _ => true, _ => false,
            pathFilter: path => path.StartsWith(@"C:\Allowed\", StringComparison.OrdinalIgnoreCase));

        Assert.AreEqual(@"C:\Allowed\cc.txt", Assert.ContainsSingle(candidates).Result.FullPath);
    }

    [TestMethod]
    public void RelatedHistoryStaysBehindADirectNameMatch()
    {
        var entries = new[] { Entry("cca", @"C:\Docs\cc.txt") };
        var related = RelatedHistoryCandidateMapper.Collect(
            FuzzyQuery.Parse("cc"), scope: null, entries,
            _ => true, _ => false);
        var direct = new SearchResultMapper.RankedCandidate(
            new AppSearchResult { Name = "cc-direct.txt", FullPath = @"C:\Docs\cc-direct.txt" },
            IsFavorite: false,
            HistoryCount: 0,
            TypeRank: int.MaxValue,
            Match: new MatchRank(MatchRank.TierName, 0, 1, 1, MatchRank.ShapeWholeWord),
            NormalizedPath: @"C:\Docs\cc-direct.txt");

        var ranked = SearchResultMapper.RankCandidates([related[0], direct]);

        Assert.AreEqual(@"C:\Docs\cc-direct.txt", ranked[0].Result.FullPath);
        Assert.AreEqual(@"C:\Docs\cc.txt", ranked[1].Result.FullPath);
    }
}
