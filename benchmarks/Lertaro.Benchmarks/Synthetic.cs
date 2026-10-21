using BenchmarkDotNet.Running;

using Lertaro.Core;
using Lertaro.Core.SearchIndex;

namespace Lertaro.Benchmarks;

// The measured part of both windows' ranking, over the same synthetic corpus on every run. See
// benchmarks/README.md for how to read the numbers.
internal static class Synthetic
{
    // Deterministic pseudo-random (xorshift64*) so a re-run measures the same data; a random corpus would
    // turn every sort number into a range.
    private const ulong Seed = 0x9E3779B97F4A7C15UL;

    /// <summary>Rows with a random packed quality key, mixed depths and name lengths, all on one drive.</summary>
    public static SearchResult[] Results(int count)
    {
        var rows = new SearchResult[count];
        var state = Seed;
        for (var i = 0; i < count; i++)
        {
            state = Next(state);
            var name = "report" + i % 977 + ".txt";
            rows[i] = new SearchResult
            {
                Name = name,
                Path = $@"D:\dir{i % 53}\sub{i % 7}\{name}",
                IsDir = i % 11 == 0,
                Drive = "D",
                RankSortKey = state,
            };
        }

        return rows;
    }

    /// <summary>Signals whose priority bucket, match strength and history score all vary.</summary>
    public static RankSignals[] Signals(int count)
    {
        var signals = new RankSignals[count];
        var state = Seed;
        for (var i = 0; i < count; i++)
        {
            state = Next(state);
            var strong = i % 3 != 0;
            signals[i] = new RankSignals(
                new MatchRank(
                    strong ? MatchRank.TierName : MatchRank.TierFullRare,
                    (int)(state >> 40) % 12,
                    0.2 + (i % 8) / 10d,
                    1d,
                    strong ? MatchRank.ShapeWholeWord : MatchRank.ShapeScattered),
                IsHistoryOnly: false,
                IsFavorite: i % 97 == 0,
                (IndexRuleEffect)(i % 4 == 0 ? 3 : i % 7 == 0 ? 1 : 2),
                false,
                i % 5 == 0 ? 100 + i % 900 : 0,
                i % 5,
                i % 11 == 0,
                "report" + i % 977,
                $@"D:\dir{i % 53}\sub{i % 7}\report{i % 977}.txt",
                68);
        }

        return signals;
    }

    /// <summary>The tiers a real budget would hand out, spread so every in-section key gets exercised.</summary>
    public static HistoryTier[] Tiers(int count)
    {
        var tiers = new HistoryTier[count];
        for (var i = 0; i < count; i++)
            tiers[i] = i % 9 == 0 ? HistoryTier.H5
                : i % 7 == 0 ? HistoryTier.H3
                : i % 4 == 0 ? HistoryTier.Tail
                : HistoryTier.None;

        return tiers;
    }

    private static ulong Next(ulong state)
    {
        state ^= state >> 12;
        state ^= state << 25;
        state ^= state >> 27;
        return state * 2685821657736338717UL;
    }
}
