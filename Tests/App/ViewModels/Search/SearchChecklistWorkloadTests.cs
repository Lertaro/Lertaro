using System.Diagnostics;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using Lertaro.App.ViewModels.Search.Mapping;
using Lertaro.App.Converters;
using Lertaro.App.ViewModels.Search;
using Lertaro.Core;
using Lertaro.Core.SearchIndex;
using Lertaro.PluginSdk.Abstractions.Plugins;

namespace Lertaro.App.Tests.ViewModels.Search;

[TestClass]
[DoNotParallelize]
public sealed class SearchChecklistWorkloadTests(TestContext context)
{
    private void Record(object value) => context.WriteLine("CHECKLIST_PERF " + JsonSerializer.Serialize(value));

    [TestMethod]
    [TestCategory("SearchChecklistPerformance")]
    [DataRow("report")]
    [DataRow("rprt")]
    public void QuickSnapshots_RecordsRepeatedFileMapping(string query)
    {
        var source = Enumerable.Range(0, 4000).Select(i => new SearchResult
            { Name = $"report-{i:D5}.txt", Path = $@"Z:\audit\report-{i:D5}.txt" }).ToList();
        // Reflection keeps this workload runnable against both saved and current product assemblies.
        var method = typeof(SearchResultMapper).GetMethod("BuildQuickResults", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        var cacheParameter = method.GetParameters().FirstOrDefault(p => p.Name == "fileMatchCache");
        var iterations = 5;
        var times = new List<double>();
        var totalBytes = 0L;
        var signatures = new HashSet<string>();
        var uniqueMatches = 0;
        for (var run = -2; run < iterations; run++)
        {
            var pass = new SearchResultMapper.InstantPassCache { Collected = true };
            var cache = cacheParameter == null ? null : Activator.CreateInstance(cacheParameter.ParameterType);
            var bytes = GC.GetTotalAllocatedBytes(true);
            var timer = Stopwatch.StartNew();
            List<AppSearchResult> final = [];
            for (var size = 200; size <= source.Count; size += 200)
            {
                var arguments = method.GetParameters().Select(p => p.HasDefaultValue ? p.DefaultValue : null).ToArray();
                arguments[0] = source.Take(size).ToList(); arguments[1] = query; arguments[4] = true;
                foreach (var parameter in method.GetParameters())
                {
                    if (parameter.Name == "instantPass") arguments[parameter.Position] = pass;
                    if (parameter.Name == "skipDisplayCap") arguments[parameter.Position] = true;
                    if (parameter.Name == "fileMatchCache") arguments[parameter.Position] = cache;
                }
                final = (List<AppSearchResult>)method.Invoke(null, arguments)!;
            }
            if (run >= 0)
            {
                times.Add(timer.Elapsed.TotalMilliseconds);
                totalBytes += GC.GetTotalAllocatedBytes(true) - bytes;
                signatures.Add(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', final.Select(r => r.FullPath))))));
            }
            Assert.HasCount(4000, final);
            if (cache != null)
                uniqueMatches = ((System.Collections.IDictionary)cache.GetType().GetField("_matches", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(cache)!).Count;
        }
        Record(new { scenario = "quick-snapshots", query, iterations, rows = 4000, paints = 20, visits = 42000,
            cached = cacheParameter != null, uniqueMatches, elapsedMs = times, allocatedBytes = totalBytes, signatures });
    }

    [TestMethod]
    [TestCategory("SearchChecklistPerformance")]
    public async Task SlowInstantProvider_RecordsFilePaintWait()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var pass = new SearchResultMapper.InstantPassCache();
        var provider = new GatedProvider(entered, release);
        var early = Task.Run(() => SearchResultMapper.CollectInstantPass(pass, "report", "report", false, null, [provider]));
        Task<List<AppSearchResult>>? paint = null;
        try
        {
            Assert.IsTrue(entered.Wait(TimeSpan.FromSeconds(10)));
            var timer = Stopwatch.StartNew();
            paint = Task.Run(() =>
            {
                var method = typeof(SearchResultMapper).GetMethod("BuildQuickResults", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
                var arguments = method.GetParameters().Select(p => p.HasDefaultValue ? p.DefaultValue : null).ToArray();
                arguments[0] = new List<SearchResult> { new() { Name = "report.txt", Path = @"Z:\report.txt" } };
                arguments[1] = "report"; arguments[4] = false;
                arguments[method.GetParameters().Single(p => p.Name == "instantPass").Position] = pass;
                return (List<AppSearchResult>)method.Invoke(null, arguments)!;
            });
            await Task.Delay(150);
            var completedDuringProviderWait = paint.IsCompleted;
            release.Set();
            await early;
            var rows = await paint;
            Record(new { scenario = "slow-instant", injectedPauseMs = 150, completedDuringProviderWait,
                paintMs = timer.Elapsed.TotalMilliseconds, calls = provider.Calls, rows = rows.Count });
            Assert.AreEqual(1, provider.Calls);
        }
        finally { release.Set(); await early; if (paint != null) await paint; }
    }

    [TestMethod]
    [TestCategory("SearchChecklistPerformance")]
    public void HighlightProviderChange_RecordsStalePlan()
    {
        var original = AliasProviderRegistry.FilterFunc;
        var provider = new QueryAliasProvider();
        AliasProviderRegistry.FilterFunc = _ => false;
        AliasProviderRegistry.Register(provider);
        try
        {
            var disabled = TextHighlighter.ComputeMask("target", "roundtwoalias").Count(value => value);
            AliasProviderRegistry.FilterFunc = candidate => ReferenceEquals(candidate, provider);
            var timer = Stopwatch.StartNew();
            var enabled = TextHighlighter.ComputeMask("target", "roundtwoalias").Count(value => value);
            Record(new { scenario = "highlight-provider", disabled, enabled, elapsedMs = timer.Elapsed.TotalMilliseconds });
        }
        finally { AliasProviderRegistry.FilterFunc = original; }
    }

    private sealed class QueryAliasProvider : IAliasProvider
    {
        public string Name => "Round two test alias";
        public string Form { get; set; } = "target";
        public IReadOnlyList<(char Start, char End)> InputRanges => [];
        public IReadOnlyList<(char Start, char End)> OutputRanges => [];
        public bool CanHandle(string text) => false;
        public IEnumerable<string> GetAliases(string text) => [];
        public IEnumerable<string> GetQueryForms(string query) => query == "roundtwoalias" ? [Form] : [];
    }

    [TestMethod]
    public void HighlightProviderEnablement_ChangesTheSameQueryAndInvalidatesCachedFileEvidence()
    {
        var original = AliasProviderRegistry.FilterFunc;
        var provider = new QueryAliasProvider();
        AliasProviderRegistry.Register(provider);
        var cache = new SearchResultMapper.QuickFileMatchCache();
        var row = new SearchResult { Name = "target", Path = @"Z:\target" };
        try
        {
            AliasProviderRegistry.FilterFunc = _ => false;
            Assert.AreEqual(0, TextHighlighter.ComputeMask("target", "roundtwoalias").Count(value => value));
            var disabled = cache.Match(row, cache.Prepare("roundtwoalias"));
            AliasProviderRegistry.FilterFunc = candidate => ReferenceEquals(candidate, provider);
            Assert.AreEqual(6, TextHighlighter.ComputeMask("target", "roundtwoalias").Count(value => value));
            Assert.AreNotEqual(disabled, cache.Match(row, cache.Prepare("roundtwoalias")));
            provider.Form = "changed";
            PluginSdk.Services.PluginSettingsService.NotifySettingChanged("round-two-test", "form", "changed");
            Assert.AreEqual(0, TextHighlighter.ComputeMask("target", "roundtwoalias").Count(value => value));
            Assert.AreEqual(7, TextHighlighter.ComputeMask("changed", "roundtwoalias").Count(value => value));
            AliasProviderRegistry.FilterFunc = _ => false;
            Assert.AreEqual(0, TextHighlighter.ComputeMask("target", "roundtwoalias").Count(value => value));
        }
        finally { AliasProviderRegistry.FilterFunc = original; }
    }

    [TestMethod]
    [TestCategory("SearchChecklistPerformance")]
    public void CatalogMatching_RepeatedSnapshots_RecordsUncachedWork()
    {
        var entries = Enumerable.Range(0, 10_000).Select(i => new SearchableItemCache.CacheEntry(
            new SearchableItem { Title = $"report-{i:D5}" }, [], null)).ToArray();
        var fuzzy = FuzzyQuery.Parse("report");
        string[] queryWords = ["report"];
        var matched = 0;
        var times = new List<double>();
        var bytes = GC.GetTotalAllocatedBytes(true);
        for (var paint = 0; paint < 20; paint++)
        {
            var timer = Stopwatch.StartNew();
            foreach (var entry in entries)
                if (SearchableItemMapper.MatchCatalogEntryWithEvidence(fuzzy, queryWords, entry, "test").Rank.IsMatch) matched++;
            times.Add(timer.Elapsed.TotalMilliseconds);
        }
        Record(new { scenario = "catalog-match", rows = 10000, paints = 20, matched, elapsedMs = times,
            allocatedBytes = GC.GetTotalAllocatedBytes(true) - bytes });
        Assert.AreEqual(200_000, matched);
    }

    private sealed class GatedProvider(ManualResetEventSlim entered, ManualResetEventSlim release) : IInstantResultProvider
    {
        public int Calls;
        public string Name => "Gated test";
        public IEnumerable<InstantResultItem> GetInstantResults(string query)
        {
            Interlocked.Increment(ref Calls);
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException();
            return [];
        }
    }
}
