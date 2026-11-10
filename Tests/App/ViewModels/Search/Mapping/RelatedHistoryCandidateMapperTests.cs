using Lertaro.App.ViewModels.Search.Mapping;
using Lertaro.Core;
using Lertaro.Core.SearchIndex;
using Lertaro.PluginSdk.Services;

namespace Lertaro.App.Tests.ViewModels.Search.Mapping;

[TestClass]
public sealed class RelatedHistoryCandidateMapperTests
{
    private static HistoryEntry Entry(string keyword, string path, int count = 3) =>
        new(keyword, path, HistoryEntryKind.File, 0, count);

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
}
