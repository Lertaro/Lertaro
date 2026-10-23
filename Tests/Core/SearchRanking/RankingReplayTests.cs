using System.Runtime.CompilerServices;
using Lertaro.Core.SearchIndex;

namespace Lertaro.Core.Tests.SearchRanking;

[TestClass]
public sealed class RankingReplayTests
{
    private static string Fixture(string name, [CallerFilePath] string source = "") =>
        Path.Combine(Path.GetDirectoryName(source)!, "Fixtures", name);

    private static IReadOnlyList<RankingCase> ReplayCases() =>
        RankingCaseFixtureLoader.LoadReplayCases(Fixture("ranking-replay-cases.v1.json"));

    [TestMethod]
    public void ReportedFixture_LoadsTheFiveEvidenceCasesWithoutRealPaths()
    {
        var cases = RankingCaseFixtureLoader.LoadReportedCases(Fixture("reported-search-cases.v1.json"));

        Assert.HasCount(5, cases);
        CollectionAssert.AreEquivalent(
            new[] { "437-cfwj-order", "437-db-intent", "437-fscapture-source", "437-pinyin-space", "440-zbl-alias-scatter" },
            cases.Select(item => item.Id).ToArray());
        Assert.IsTrue(cases.SelectMany(item => item.SyntheticPaths)
            .All(path => path.StartsWith(@"X:\Fixture\", StringComparison.Ordinal)));
        Assert.IsTrue(cases.Single(item => item.Id == "437-fscapture-source").Queries.Contains(null));
    }

    [TestMethod]
    public void ReplayFixture_LoadsSyntheticPixPinAndPairedHistoryCases()
    {
        var cases = ReplayCases();

        Assert.HasCount(4, cases);
        var pixpin = cases.Single(item => item.Id == "pixpin-appdata-shortcut");
        Assert.AreEqual("pixpin", pixpin.RawQuery);
        Assert.AreEqual("Application", pixpin.TypeOrder[0]);
        Assert.AreEqual(IndexRuleEffect.Low, pixpin.Candidates.Single(row => row.Token == "pixpin-start-menu").Priority);
        Assert.IsTrue(cases.SelectMany(item => item.Candidates)
            .All(row => row.SyntheticPath.StartsWith(@"X:\Fixture\", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void V2Baseline_PinsKnownPassAndKnownInversions()
    {
        var runs = ReplayCases().ToDictionary(
            item => item.Id,
            item => V2OfflineRankingPolicy.Instance.Rank(item),
            StringComparer.Ordinal);

        CollectionAssert.AreEqual(
            new[] { "pixpin-folder", "pixpin-exe", "pixpin-start-menu" },
            runs["pixpin-appdata-shortcut"].Tokens.ToArray());
        CollectionAssert.AreEqual(
            new[] { "atlas-shortcut", "atlas-exe" },
            runs["shortcut-normal-root"].Tokens.ToArray());
        CollectionAssert.AreEqual(
            new[] { "editor-history-weaker", "editor-exact" },
            runs["direct-history-overreach"].Tokens.ToArray());
        CollectionAssert.AreEqual(
            new[] { "notes-learned-alias", "notes-direct" },
            runs["learned-alias-overreach"].Tokens.ToArray());
    }

    [TestMethod]
    public void Metrics_MatchHandCalculatedSingleRelevantRowExample()
    {
        var rankingCase = ReplayCases().Single(item => item.Id == "shortcut-normal-root");
        var baseline = V2OfflineRankingPolicy.Instance.Rank(rankingCase);
        var reversed = new RankingRun("reversed", baseline.Rows
            .Reverse()
            .Select((row, index) => row with { Rank = index + 1 })
            .ToArray());

        Assert.AreEqual(0, RankingMetrics.RecallAt(reversed, rankingCase.RelevantTokens, 1), 0.0000001);
        Assert.AreEqual(1, RankingMetrics.RecallAt(reversed, rankingCase.RelevantTokens, 2), 0.0000001);
        Assert.AreEqual(0.5, RankingMetrics.ReciprocalRank(reversed, rankingCase.RelevantTokens), 0.0000001);
        Assert.AreEqual(1 / Math.Log2(3), RankingMetrics.NdcgAt(reversed, 2), 0.0000001);
        Assert.AreEqual(1, RankingMetrics.PairwiseInversions(reversed, rankingCase.MustAppearBefore));
    }

    [TestMethod]
    public void ReplayComparison_ReportsSignedTargetRankDelta()
    {
        var rankingCase = ReplayCases().Single(item => item.Id == "pixpin-appdata-shortcut");
        var baseline = V2OfflineRankingPolicy.Instance.Rank(rankingCase);
        var keys = baseline.Rows.ToDictionary(row => row.Candidate.Token, row => row.Key, StringComparer.Ordinal);
        var relevanceFirst = new DelegateOfflineRankingPolicy("relevance-first", item => item.Candidates
            .OrderByDescending(row => row.Relevance)
            .ThenBy(row => row.Token, StringComparer.Ordinal)
            .Select(row => (row, keys[row.Token]))
            .ToArray());

        var comparison = RankingReplay.Compare(rankingCase, V2OfflineRankingPolicy.Instance, relevanceFirst);

        Assert.AreEqual(-2, comparison.TargetRankDelta("pixpin-start-menu"));
        Assert.IsTrue(RankingMetrics.MeetsAssertions(rankingCase, comparison.Candidate));
    }

    [TestMethod]
    public void V2Replay_IsIndependentOfCandidateEnumerationOrder()
    {
        var rankingCase = ReplayCases().Single(item => item.Id == "pixpin-appdata-shortcut");
        var expected = V2OfflineRankingPolicy.Instance.Rank(rankingCase).Tokens.ToArray();

        foreach (var permutation in Permutations(rankingCase.Candidates.ToArray(), 0))
        {
            var shuffled = rankingCase with { Candidates = permutation };
            CollectionAssert.AreEqual(expected, V2OfflineRankingPolicy.Instance.Rank(shuffled).Tokens.ToArray());
        }
    }

    [TestMethod]
    public void V2ReplayKeys_AreAntisymmetricAndTransitive()
    {
        var keys = ReplayCases()
            .SelectMany(item => V2OfflineRankingPolicy.Instance.Rank(item).Rows)
            .Select(row => row.Key)
            .ToArray();
        var comparer = RankKeyComparer.Instance;

        foreach (var left in keys)
        foreach (var right in keys)
            Assert.AreEqual(
                Math.Sign(comparer.Compare(left, right)),
                -Math.Sign(comparer.Compare(right, left)));

        foreach (var first in keys)
        foreach (var second in keys)
        foreach (var third in keys)
        {
            if (comparer.Compare(first, second) <= 0 && comparer.Compare(second, third) <= 0)
                Assert.IsTrue(comparer.Compare(first, third) <= 0);
        }
    }

    private static IEnumerable<CandidateSnapshot[]> Permutations(CandidateSnapshot[] values, int start)
    {
        if (start == values.Length)
        {
            yield return values.ToArray();
            yield break;
        }

        for (var i = start; i < values.Length; i++)
        {
            (values[start], values[i]) = (values[i], values[start]);
            foreach (var permutation in Permutations(values, start + 1))
                yield return permutation;
            (values[start], values[i]) = (values[i], values[start]);
        }
    }
}
