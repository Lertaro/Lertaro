using System.Collections.Specialized;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows.Data;
using Lertaro.App.Helpers;
using Lertaro.App.ViewModels.Search;
using Lertaro.App.ViewModels.Search.DynamicSidebar;
using Lertaro.App.ViewModels.Search.Mapping;
using Lertaro.Core;
using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Services;

namespace Lertaro.App.Tests.ViewModels.Search;

[STATestClass]
[DoNotParallelize]
public sealed class SearchPerformanceWorkloadTests(TestContext context)
{
    [TestMethod]
    [TestCategory("SearchPerformance")]
    public void RelatedHistory_LargeCatalog_SelectsTwoWithoutSortingEveryMatch()
    {
        const int count = 50_000;
        var entries = Enumerable.Range(0, count)
            .Select(i => new HistoryEntry(
                $"related-{i:D6}", $@"Z:\History\{i:D6}.txt", HistoryEntryKind.File, i, 3))
            .ToArray();
        var catalog = RelatedHistoryCandidateMapper.BuildCatalog(version: 1, entries).Entries;

        for (var i = 0; i < 3; i++)
            RelatedHistoryCandidateMapper.MatchCatalog("related", catalog, RelatedHistoryCandidateMapper.MaxCandidates);

        var bytes = GC.GetTotalAllocatedBytes(precise: true);
        var timer = Stopwatch.StartNew();
        IReadOnlyList<RelatedHistoryCandidateMapper.CatalogEntry> final = [];
        for (var i = 0; i < 20; i++)
            final = RelatedHistoryCandidateMapper.MatchCatalog(
                "related", catalog, RelatedHistoryCandidateMapper.MaxCandidates);
        timer.Stop();
        bytes = GC.GetTotalAllocatedBytes(precise: true) - bytes;

        Assert.HasCount(RelatedHistoryCandidateMapper.MaxCandidates, final);
        context.WriteLine("SEARCH_PERF " + JsonSerializer.Serialize(new
        {
            scenario = "related-history-scan",
            rows = count,
            iterations = 20,
            elapsedMs = timer.Elapsed.TotalMilliseconds,
            allocatedBytes = bytes,
            resultCount = final.Count
        }));
    }

    // Scenario measurements, not timing assertions: keep the same workload for before/after builds.
    [TestMethod]
    [TestCategory("SearchPerformance")]
    [DataRow(20_000)]
    [DataRow(100_000)]
    public void SidebarAndCollection_RepeatedSnapshots_PreserveFinalResults(int count)
    {
        var all = Enumerable.Range(0, count).Select(i => new AppSearchResult
        {
            Name = $"report-{count - i:D6}",
            FullPath = $@"Z:\Archive\report-{count - i:D6}.txt",
            ResultKind = "File",
            SearchQuery = "report"
        }).ToList();
        var expected = all.Where((_, i) => i % 10 == 0).OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
        var admitted = expected.ToHashSet();
        var collection = new ObservableRangeCollection<AppSearchResult>();
        var view = new ListCollectionView(collection);
        var coordinator = new DynamicFilterCoordinator();
        var renders = 0;
        var sorts = 0;
        var resets = 0;
        collection.CollectionChanged += (_, e) => { if (e.Action == NotifyCollectionChangedAction.Reset) resets++; };
        List<Func<IReadOnlyList<ISearchResult>, Task<IReadOnlyList<ISearchResult>>>> filters =
            [rows => Task.FromResult<IReadOnlyList<ISearchResult>>(rows.Cast<AppSearchResult>().Where(admitted.Contains).ToList())];
        void Paint() => coordinator.Apply(all, filters,
            rows => { sorts++; return SearchResultSorter.Sort(rows, "Name", true).ToList(); }, () => all,
            rows => { renders++; collection.ReconcileTo(rows, SearchResultsReconciler.ItemsEqual); }, _ => { });

        for (var i = 0; i < 3; i++) Paint();
        renders = sorts = resets = 0;
        var bytes = GC.GetTotalAllocatedBytes(true);
        var timer = Stopwatch.StartNew();
        for (var i = 0; i < 20; i++) Paint();
        timer.Stop();
        bytes = GC.GetTotalAllocatedBytes(true) - bytes;

        CollectionAssert.AreEqual(expected, collection.ToArray());
        Assert.AreEqual(expected.Length, view.Count);
        context.WriteLine("SEARCH_PERF " + JsonSerializer.Serialize(new
        {
            scenario = "sidebar-sort-reconcile",
            rows = count,
            iterations = 20,
            elapsedMs = timer.Elapsed.TotalMilliseconds,
            allocatedBytes = bytes,
            renders,
            sorts,
            resets,
            resultCount = collection.Count,
            signature = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', collection.Select(r => r.FullPath)))))
        }));
    }
}
