using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Reflection;
using Lertaro.Core.IndexV2.Search;
using Lertaro.Core.SearchIndex.Fzf;

namespace Lertaro.Core.Tests.IndexV2.Search;

[TestClass]
[DoNotParallelize]
public sealed class SearchPerformanceWorkloadTests(TestContext context)
{
    [TestMethod]
    [TestCategory("SearchPerformance")]
    public void Refinement_AlreadyCancelledWork_RecordsRemainingCost()
    {
        const int count = 60_000;
        var records = new List<FileRecord> { LiveIndexFixture.Root() };
        records.AddRange(Enumerable.Range(0, count).Select(i => new FileRecord((ulong)i + 2, 1,
            $"report-{i:D6}.txt", FileRecordFlags.None)));
        using var fixture = LiveIndexFixture.Build("Z", records);
        fixture.Index.Read<object?>((snapshot, delta) =>
        {
            foreach (var type in new[] { typeof(NameSearch), typeof(Core.IndexV2.Search.PathMode.PathSearchFuzzy) })
            {
                // Reflection lets the exact same workload run against the saved pre-fix DLL, whose
                // refinement method did not accept cancellation at all. No old algorithm is reimplemented.
                var method = type.GetMethod(type == typeof(NameSearch) ? "RefineRanks" : "RefineWithWeight", BindingFlags.Static | BindingFlags.NonPublic)!;
                var observesCancellation = method.GetParameters()[^1].ParameterType == typeof(CancellationToken);
                var pattern = FzfPattern.Parse("report");
                var times = new List<double>();
                var rewritten = 0;
                var cancelled = 0;
                for (var iteration = -3; iteration < 20; iteration++)
                {
                    var ranks = Enumerable.Range(1, count).Select(row => new FzfRank(row, 0, 0)).ToList();
                    var parameters = new List<object?> { snapshot, delta };
                    if (type != typeof(NameSearch)) parameters.Add(null);
                    parameters.Add(pattern);
                    parameters.Add(ranks);
                    if (observesCancellation) parameters.Add(new CancellationToken(canceled: iteration >= 0));
                    var arguments = parameters.ToArray();
                    var timer = Stopwatch.StartNew();
                    try { method.Invoke(null, arguments); }
                    catch (TargetInvocationException ex) when (ex.InnerException is OperationCanceledException)
                    {
                        if (iteration >= 0) cancelled++;
                    }
                    timer.Stop();
                    if (iteration >= 0)
                    {
                        times.Add(timer.Elapsed.TotalMilliseconds);
                        rewritten += ranks.Count(rank => rank.SortKey != 0);
                    }
                }
                context.WriteLine("SEARCH_PERF " + JsonSerializer.Serialize(new
                {
                    scenario = "cancelled-refinement",
                    stage = type.Name,
                    rows = count,
                    iterations = 20,
                    observesCancellation,
                    elapsedMs = times,
                    cancelled,
                    rewritten
                }));
                Assert.AreEqual(observesCancellation ? 20 : 0, cancelled);
                if (observesCancellation) Assert.AreEqual(0, rewritten);
            }
            return null;
        });
    }

    [TestMethod]
    [TestCategory("SearchPerformance")]
    public void Search_FixedCorpus_RecordsLatencyAllocationsAndOrderedResults()
    {
        const int count = 60_000;
        var records = new List<FileRecord> { LiveIndexFixture.Root() };
        for (var i = 0; i < 100; i++)
            records.Add(new FileRecord((ulong)(i + 2), 1, $"Archive{i:D3}", FileRecordFlags.Directory));
        for (var i = 0; i < count; i++)
            records.Add(new FileRecord((ulong)(i + 102), (ulong)(i / 600 + 2), $"report-{i:D6}.{(i % 10 == 0 ? "md" : "txt")}", FileRecordFlags.None));
        using var fixture = LiveIndexFixture.Build("Z", records);
        foreach (var (query, limit, filter) in new (string, int, string?)[]
        {
            ("report", 204, null), ("report", int.MaxValue, null),
            ("archive report", int.MaxValue, null), (@"archive\report", int.MaxValue, null), (@"archive\report", 204, "*.txt"),
            (@"z:\archive000\", 204, "*.txt")
        })
        {
            List<SearchResult> Search()
            {
                var results = new List<SearchResult>();
                IndexV2Searcher.SearchStreaming(fixture.Index, query, limit, results.Add, CancellationToken.None, fileNameFilter: filter);
                return results;
            }
            for (var i = 0; i < 3; i++) Search();
            var collections = Enumerable.Range(0, 3).Select(GC.CollectionCount).ToArray();
            var bytes = GC.GetTotalAllocatedBytes(true);
            var elapsed = new List<double>();
            var first = new List<double>();
            List<SearchResult> final = [];
            for (var i = 0; i < 10; i++)
            {
                final = [];
                var timer = Stopwatch.StartNew();
                IndexV2Searcher.SearchStreaming(fixture.Index, query, limit, r =>
                {
                    if (final.Count == 0) first.Add(timer.Elapsed.TotalMilliseconds);
                    final.Add(r);
                }, CancellationToken.None, fileNameFilter: filter);
                elapsed.Add(timer.Elapsed.TotalMilliseconds);
            }
            bytes = GC.GetTotalAllocatedBytes(true) - bytes;
            Assert.AreEqual(final.Count, final.Select(r => r.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count());
            if (filter == null) Assert.AreEqual(Math.Min(count, limit), final.Count);
            var signature = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', final.Select(r => $"{r.Path}|{r.IsDir}|{r.RankSortKey}")))));
            context.WriteLine("SEARCH_PERF " + JsonSerializer.Serialize(new
            {
                scenario = "index",
                rows = count,
                query,
                limit,
                filter,
                iterations = 10,
                elapsedMs = elapsed,
                firstResultMs = first,
                allocatedBytes = bytes,
                gcCollections = Enumerable.Range(0, 3).Select(g => GC.CollectionCount(g) - collections[g]).ToArray(),
                resultCount = final.Count,
                signature,
                acceptedFiles = final.Count(r => !r.IsDir && (filter == null || r.Name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)))
            }));
        }
    }

    [TestMethod]
    [TestCategory("SearchPerformance")]
    public void QueryPageCache_RecordsColdAndHitLatencyAllocationsAndRetainedRows()
    {
        const int count = 60_000;
        const int limit = 204;
        var records = new List<FileRecord> { LiveIndexFixture.Root() };
        records.AddRange(Enumerable.Range(0, count).Select(i => new FileRecord((ulong)i + 2, 1,
            $"report-{i:D6}.txt", FileRecordFlags.None)));
        using var fixture = LiveIndexFixture.Build("Z", records);

        List<SearchResult> Search()
        {
            var results = new List<SearchResult>();
            IndexV2Searcher.SearchStreaming(
                fixture.Index, "report", limit, results.Add, CancellationToken.None);
            return results;
        }

        var memoryBefore = GC.GetTotalMemory(forceFullCollection: true);
        var coldTimer = Stopwatch.StartNew();
        var cold = Search();
        coldTimer.Stop();
        var memoryAfterCold = GC.GetTotalMemory(forceFullCollection: true);
        var allocatedBeforeHits = GC.GetTotalAllocatedBytes(precise: true);
        var replaysBefore = fixture.Index.SearchPages.ReplayCount;
        var hitTimes = new List<double>(10);
        List<SearchResult>? final = null;
        for (var i = 0; i < 10; i++)
        {
            var timer = Stopwatch.StartNew();
            final = Search();
            timer.Stop();
            hitTimes.Add(timer.Elapsed.TotalMilliseconds);
        }
        var allocatedByHits = GC.GetTotalAllocatedBytes(precise: true) - allocatedBeforeHits;

        Assert.AreEqual(limit, cold.Count);
        Assert.AreEqual(limit, final!.Count);
        Assert.AreEqual(204, fixture.Index.SearchPages.RetainedRowCount);
        // The benchmark must fail if replay stops working; counts alone pass even when every "hit"
        // falls through to a full search.
        Assert.AreEqual(10L, fixture.Index.SearchPages.ReplayCount - replaysBefore);
        Assert.IsTrue(cold.Select(row => (row.Path, row.IsDir, row.RankSortKey))
            .SequenceEqual(final.Select(row => (row.Path, row.IsDir, row.RankSortKey))));
        context.WriteLine("SEARCH_PERF " + JsonSerializer.Serialize(new
        {
            scenario = "query-page-cache",
            rows = count,
            limit,
            coldMs = coldTimer.Elapsed.TotalMilliseconds,
            hitMs = hitTimes,
            requestedQueries = 11,
            expectedHits = 10,
            hitRate = 10d / 11d,
            allocatedByHits,
            retainedEntries = fixture.Index.SearchPages.RetainedEntryCount,
            retainedRows = fixture.Index.SearchPages.RetainedRowCount,
            memoryDeltaBytes = memoryAfterCold - memoryBefore,
            theoreticalMaximumRetainedRows = 128 * QueryPageCache.MaxRows
        }));
    }
}
