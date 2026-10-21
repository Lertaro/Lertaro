using BenchmarkDotNet.Attributes;

using Lertaro.Core.SearchIndex;

namespace Lertaro.Benchmarks;

/// <summary>
/// The quick window's kernel: decorate every candidate into a key, then sort keys and rows together
/// (App/ViewModels/Search/Mapping/RankedCandidateOrdering.Order). It replaced an 11-level LINQ chain that
/// re-derived its keys on every comparison, so Allocated is as interesting as Mean here.
/// </summary>
/// <remarks>
/// The history budget is not measured: it is one linear pass over the same rows and is asserted separately
/// by Tests/Core/SearchIndex/HistoryBudgetTests.
/// </remarks>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 2, iterationCount: 5)]
[InProcess]
public class QuickRankBenchmarks
{
    private RankSignals[] _pristine = [];
    private RankSignals[] _signals = [];
    private RankKey[] _keys = [];
    private HistoryTier[] _tiers = [];

    [Params(204, 4_000)]
    public int Rows { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _pristine = Synthetic.Signals(Rows);
        _tiers = Synthetic.Tiers(Rows);
        _signals = new RankSignals[Rows];
        _keys = new RankKey[Rows];
    }

    [IterationSetup]
    public void Reset()
    {
        // The sort permutes both arrays, so every iteration starts from the same unsorted input.
        Array.Copy(_pristine, _signals, _pristine.Length);
        Array.Clear(_keys);
    }

    [Benchmark]
    public RankKey[] DecorateAndSort()
    {
        for (var i = 0; i < _signals.Length; i++)
            _keys[i] = RankPolicy.BuildKey(_signals[i], _tiers[i]);

        Array.Sort(_keys, _signals, RankKeyComparer.Instance);
        return _keys;
    }
}
