using System.Runtime.CompilerServices;
using Lertaro.Core.SearchIndex;

namespace Lertaro.Core.Tests.SearchRanking;

[TestClass]
public sealed class ShadowV3EvaluationTests
{
    private static string Fixture(string name, [CallerFilePath] string source = "") =>
        Path.Combine(Path.GetDirectoryName(source)!, "Fixtures", name);

    private static string ShadowCasesPath => Fixture("ranking-shadow-v3-cases.v1.json");

    private static string BaselineCasesPath => Fixture("ranking-replay-cases.v1.json");

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
            Assert.AreEqual(
                0,
                v3Metrics.MissingExpectedTokens,
                $"Case '{rankingCase.Id}' lost an expected token under V3: {string.Join(", ", comparison.Candidate.Tokens)}");
            Assert.IsGreaterThanOrEqualTo(
                v2Metrics.NdcgAt10,
                v3Metrics.NdcgAt10 + 1e-9,
                $"Case '{rankingCase.Id}' regressed nDCG@10 from {v2Metrics.NdcgAt10:0.000} to {v3Metrics.NdcgAt10:0.000}");

            AssertDeclaredGrades(rankingCase, comparison.Candidate);
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

    // The ladder itself, not only the pairwise order it produces: each candidate may name the grade V3 has to
    // resolve for it, which is what pins Grade 0 to a real joined witness rather than to a full-coverage mask.
    private static void AssertDeclaredGrades(RankingCase rankingCase, RankingRun run)
    {
        foreach (var row in run.Rows)
        {
            var candidate = row.Candidate;
            if (candidate.ExpectedV3Grade is not { } expected)
                continue;

            var key = RankPolicyV3.BuildKey(
                candidate.ToV3Signals(), HistoryTier.None, candidate.ToEvidence(rankingCase.RawQuery));
            Assert.AreEqual(
                expected,
                key.Grade,
                $"'{candidate.Token}' in '{rankingCase.Id}' graded {key.Grade}, the fixture declared {expected}");
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
