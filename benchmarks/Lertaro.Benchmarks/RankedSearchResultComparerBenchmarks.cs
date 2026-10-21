using BenchmarkDotNet.Attributes;

using Lertaro.Core;
using Lertaro.Core.SearchIndex;
using PriorityVerdict = Lertaro.Core.ExclusionRuleSet.PriorityVerdict;

namespace Lertaro.Benchmarks;

/// <summary>
/// The full window's final order over already-decorated rows (App/ViewModels/Search/Mapping/
/// StreamingResultAccumulator merges each arriving batch against this). Decorating happens once per row on
/// arrival, so it is measured in GlobalSetup -- the number that matters is the comparison the window runs
/// millions of times, and the sort's allocation while a hundred thousand rows are already on screen.
/// </summary>
[InProcess]
[MemoryDiagnoser]
[SimpleJob(warmupCount: 2, iterationCount: 5)]
public class RankedSearchResultComparerBenchmarks
{
    private const int Rows = 100_000;

    private SearchResult[] _all = [];
    private SearchResult[] _work = [];

    [GlobalSetup]
    public void Setup()
    {
        _all = Synthetic.Results(Rows);

        // A query-level context like the real one: a fixed rule set and history for a handful of paths.
        var scores = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < _all.Length; i += 500)
            scores[_all[i].Path] = i % 1000 == 0 ? 900 : 350;
        var context = new RankContext(
            (_, _) => new PriorityVerdict(IndexRuleEffect.Normal, false),
            scores);

        foreach (var row in _all)
            SearchResultRanking.Decorate(row, context);

        _work = new SearchResult[Rows];
    }

    [IterationSetup]
    public void Reset() => Array.Copy(_all, _work, _all.Length);

    [Benchmark]
    public SearchResult[] SortDecoratedRows()
    {
        Array.Sort(_work, RankedSearchResultComparer.Instance);
        return _work;
    }
}
