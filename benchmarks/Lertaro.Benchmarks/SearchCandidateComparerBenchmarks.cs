using BenchmarkDotNet.Attributes;

using Lertaro.Core;
using Lertaro.Core.SearchIndex;

namespace Lertaro.Benchmarks;

/// <summary>
/// The engine's truncation step: order every drive's buffered rows and keep the best few
/// (Core/Indexer/Usn/SearchCoordinator). Baseline is the comparer this used to share with the final
/// ranking, so the two columns say directly whether swapping it cost anything.
/// </summary>
[InProcess]
[MemoryDiagnoser]
[SimpleJob(warmupCount: 2, iterationCount: 5)]
public class SearchCandidateComparerBenchmarks
{
    private SearchResult[] _all = [];
    private SearchResult[] _work = [];

    [Params(4_000, 100_000)]
    public int Rows { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _all = Synthetic.Results(Rows);
        _work = new SearchResult[Rows];
    }

    [IterationSetup]
    public void Reset() => Array.Copy(_all, _work, _all.Length);

    [Benchmark(Baseline = true)]
    public SearchResult[] CandidateComparer()
    {
        Array.Sort(_work, SearchCandidateComparer.Instance);
        return _work;
    }

    [Benchmark]
    public SearchResult[] LegacyRankComparer()
    {
        Array.Sort(_work, SearchResultRankComparer.Instance);
        return _work;
    }
}
