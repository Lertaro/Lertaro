using Lertaro.Core;
using Lertaro.Core.SearchIndex;

namespace Lertaro.Core.Tests.SearchRanking;

[TestClass]
public sealed class ShadowV3EvaluationTests
{
    private static string ShadowCasesPath =>
        Path.Combine(RankingCaseFixtureLoader.SearchRankingRoot, "Fixtures", "ranking-shadow-v3-cases.v1.json");

    private static string BaselineCasesPath =>
        Path.Combine(RankingCaseFixtureLoader.SearchRankingRoot, "Fixtures", "ranking-replay-cases.v1.json");

    [TestMethod]
    public void ShadowV3_PassesAllBaselineAndBoundaryFixtures_WithZeroWeakOverStrongInversions()
    {
        var allCases = RankingCaseFixtureLoader.LoadReplayCases(BaselineCasesPath)
            .Concat(RankingCaseFixtureLoader.LoadReplayCases(ShadowCasesPath))
            .ToArray();

        foreach (var rankingCase in allCases)
        {
            var comparison = RankingReplay.Compare(
                rankingCase,
                V2OfflineRankingPolicy.Instance,
                V3OfflineRankingPolicy.Instance);
            var v2Metrics = RankingMetrics.Evaluate(rankingCase, comparison.Baseline);
            var v3Metrics = RankingMetrics.Evaluate(rankingCase, comparison.Candidate);

            Assert.AreEqual(
                0,
                v3Metrics.PairwiseInversions,
                $"Case '{rankingCase.Id}' had pairwise inversions under V3: {string.Join(", ", comparison.Candidate.Tokens)}");
            Assert.IsGreaterThanOrEqualTo(
                v2Metrics.NdcgAt10,
                v3Metrics.NdcgAt10 + 1e-9,
                $"Case '{rankingCase.Id}' regressed nDCG@10 from {v2Metrics.NdcgAt10:0.000} to {v3Metrics.NdcgAt10:0.000}");

            foreach (var topK in rankingCase.MustBeTopK)
            {
                var rank = comparison.Candidate.RankOf(topK.Token);
                Assert.IsGreaterThan(0, rank, $"Expected '{topK.Token}' in '{rankingCase.Id}'");
                Assert.IsLessThanOrEqualTo(
                    topK.K,
                    rank,
                    $"Expected '{topK.Token}' within top {topK.K} in '{rankingCase.Id}', actual rank {rank}");
            }

            foreach (var absent in rankingCase.MustBeAbsent)
                Assert.AreEqual(0, comparison.Candidate.RankOf(absent), $"Unexpected absent token '{absent}'");

            AssertNoOrdinaryWeakOverStrongInversion(rankingCase, comparison.Candidate);
        }
    }

    private static void AssertNoOrdinaryWeakOverStrongInversion(RankingCase rankingCase, RankingRun run)
    {
        var grades = run.Rows.Select(row =>
        {
            var signals = row.Candidate.ToV3Signals();
            var evidence = row.Candidate.ToEvidence(rankingCase.RawQuery);
            return (
                Row: row.Candidate,
                Key: RankPolicyV3.BuildKey(signals, HistoryTier.None, evidence));
        }).ToArray();

        for (var i = 0; i < grades.Length; i++)
        {
            for (var j = i + 1; j < grades.Length; j++)
            {
                var earlier = grades[i];
                var later = grades[j];
                // Explicit AlwaysTop pins, strong Favorite pins, and explicit Low demotions are deliberate
                // section boundaries; within ordinary High/Normal items, weaker evidence may never lead stronger.
                if (earlier.Key.Section != (byte)RankSection.Boosted
                    || later.Key.Section != (byte)RankSection.Boosted)
                {
                    continue;
                }

                Assert.IsLessThanOrEqualTo(
                    (int)later.Key.Grade,
                    (int)earlier.Key.Grade,
                    $"Ordinary candidate '{earlier.Row.Token}' ({earlier.Key.Grade}) preceded stronger '{later.Key.Grade}' ('{later.Row.Token}') in '{rankingCase.Id}'");
            }
        }
    }
}
