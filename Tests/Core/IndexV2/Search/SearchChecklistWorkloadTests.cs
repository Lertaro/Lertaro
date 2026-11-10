using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lertaro.Core.IndexV2.Search;
using Lertaro.Core.SearchIndex.Fzf;
using Lertaro.Core.Services.Search;

namespace Lertaro.Core.Tests.IndexV2.Search;

[TestClass]
[DoNotParallelize]
public sealed class SearchChecklistWorkloadTests(TestContext context)
{
    private void Record(object result) => context.WriteLine("CHECKLIST_PERF " + JsonSerializer.Serialize(result));

    [TestMethod]
    [TestCategory("SearchChecklistPerformance")]
    public void TopN_ShuffledTies_RecordsStabilityAndCost()
    {
        foreach (var tied in new[] { true, false })
        {
            var inputs = Enumerable.Range(0, 100).Select(seed =>
            {
                var values = Enumerable.Range(0, 20_000).Select(i => new FzfRank(i, i, tied ? 7UL : (ulong)(i % 97))).ToArray();
                new Random(seed).Shuffle(values);
                return values;
            }).ToArray();
            var signatures = new HashSet<string>();
            for (var warmup = 0; warmup < 3; warmup++)
            {
                var top = new FzfTopN(204);
                foreach (var rank in inputs[0]) top.Add(rank);
                top.Finish(204);
            }
            var times = new List<double>();
            var allocations = GC.GetTotalAllocatedBytes(true);
            foreach (var input in inputs)
            {
                var timer = Stopwatch.StartNew();
                var top = new FzfTopN(204);
                foreach (var rank in input) top.Add(rank);
                var results = top.Finish(204);
                times.Add(timer.Elapsed.TotalMilliseconds);
                signatures.Add(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join(',', results.Select(r => r.EntryIndex))))));
                Assert.HasCount(204, results);
            }
            Record(new { scenario = "topn-shuffle", tied, rows = 20_000, iterations = 100, elapsedMs = times,
                allocatedBytes = GC.GetTotalAllocatedBytes(true) - allocations, distinctResults = signatures.Count });
        }
    }

    [TestMethod]
    [TestCategory("SearchChecklistPerformance")]
    public void LiveDirectory_DirectListingAndMetadata_RecordsWork()
    {
        var directory = Directory.CreateTempSubdirectory("lertaro-checklist-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(directory, "report.txt"), new string('x', 123));
            for (var folder = 0; folder < 10; folder++)
            {
                var child = Directory.CreateDirectory(Path.Combine(directory, $"child{folder}"));
                for (var file = 0; file < 100; file++) File.WriteAllText(Path.Combine(child.FullName, $"report{file}.txt"), "test");
            }
            var times = new List<double>();
            var bytes = GC.GetTotalAllocatedBytes(true);
            List<SearchResult> entries = [];
            var delivered = 0;
            for (var i = -2; i < 10; i++)
            {
                delivered = 0;
                var timer = Stopwatch.StartNew();
                entries = LiveDirectorySearcher.ScanDirectory(directory, 10_000, CancellationToken.None,
                    liveQuery: "", onLiveMatch: _ => delivered++, onlyDirectChildren: true, parentPath: directory);
                if (i >= 0) times.Add(timer.Elapsed.TotalMilliseconds);
            }
            Record(new { scenario = "live-direct", iterations = 10, elapsedMs = times, enumerated = entries.Count, delivered,
                allocatedBytesIncludingWarmups = GC.GetTotalAllocatedBytes(true) - bytes,
                metadataSize = entries.Single(r => r.Name == "report.txt").Metadata.Size });
            Assert.AreEqual(11, delivered);
        }
        finally { Directory.Delete(directory, true); }
    }

    [TestMethod]
    [TestCategory("SearchChecklistPerformance")]
    public async Task Backpressure_BlockedConsumer_RecordsIndexWriterWait()
    {
        using var fixture = LiveIndexFixture.Build("Z", [LiveIndexFixture.Root(), .. Enumerable.Range(0, 3000)
            .Select(i => new FileRecord((ulong)i + 2, 1, $"report{i}.txt", FileRecordFlags.None))]);
        using var cancellation = new CancellationTokenSource();
        var channel = SearchStreamPump.CreateResultChannel();
        var blocked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var waits = 0;
        var producer = Task.Run(() => IndexV2Searcher.SearchStreaming(fixture.Index, "report", int.MaxValue, row =>
        {
            var write = channel.Writer.WriteAsync(row, cancellation.Token);
            if (!write.IsCompletedSuccessfully) { Interlocked.Increment(ref waits); blocked.TrySetResult(); }
            write.AsTask().GetAwaiter().GetResult();
        }, cancellation.Token));
        Task? writer = null;
        var clock = new Stopwatch();
        try
        {
            await blocked.Task.WaitAsync(TimeSpan.FromSeconds(10));
            writer = Task.Run(() => { clock.Start(); writerStarted.SetResult(); fixture.Index.Mutate((_, _) => { }); clock.Stop(); });
            await writerStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await Task.Delay(150);
            var completedWhileBlocked = writer.IsCompleted;
            var stop = Stopwatch.StartNew();
            cancellation.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(async () => await producer);
            await writer.WaitAsync(TimeSpan.FromSeconds(10));
            Record(new { scenario = "backpressure", injectedConsumerPauseMs = 150, waits,
                completedWhileBlocked, writerWaitMs = clock.Elapsed.TotalMilliseconds, cancelToReleaseMs = stop.Elapsed.TotalMilliseconds });
        }
        finally
        {
            cancellation.Cancel();
            try { await producer; } catch (OperationCanceledException) { }
            if (writer != null) await writer;
        }
    }

    [TestMethod]
    [TestCategory("SearchChecklistPerformance")]
    public void TokenCandidateLimit_QualifyingTail_RecordsLostRecall()
    {
        using var fixture = LiveIndexFixture.Build("Z", [LiveIndexFixture.Root(), .. Enumerable.Range(0, 6000)
            .Select(i => new FileRecord((ulong)i + 2, 1, i < 5900 ? $"report{i:D4}" : $"report-long-target-{i:D4}.mp3", FileRecordFlags.None))]);
        var bounded = new List<SearchResult>();
        var pushed = new List<SearchResult>();
        var timer = Stopwatch.StartNew();
        IndexV2Searcher.SearchStreaming(fixture.Index, "report", 4000, bounded.Add, CancellationToken.None);
        var boundedMs = timer.Elapsed.TotalMilliseconds;
        timer.Restart();
        IndexV2Searcher.SearchStreaming(fixture.Index, "report", 4000, pushed.Add, CancellationToken.None, fileNameFilter: "*.mp3");
        Record(new { scenario = "token-tail", originalMatches = 6000, qualifying = 100,
            postFilterCount = bounded.Count(r => r.Name.EndsWith(".mp3", StringComparison.Ordinal)), pushedCount = pushed.Count,
            boundedMs, pushedMs = timer.Elapsed.TotalMilliseconds });
        Assert.HasCount(100, pushed);
    }

    [TestMethod]
    [TestCategory("SearchChecklistPerformance")]
    public void LiveDirectory_EnumerationCap_RecordsSilentTruncation()
    {
        var directory = Directory.CreateTempSubdirectory("lertaro-checklist-cap-").FullName;
        try
        {
            for (var i = 0; i < 10_020; i++) File.WriteAllText(Path.Combine(directory, $"file{i:D5}.txt"), "");
            var timer = Stopwatch.StartNew();
            var rows = LiveDirectorySearcher.ScanDirectory(directory, 10_000, CancellationToken.None);
            Record(new { scenario = "live-cap", existing = 10020, returned = rows.Count, elapsedMs = timer.Elapsed.TotalMilliseconds });
            Assert.HasCount(10_000, rows);
        }
        finally { Directory.Delete(directory, true); }
    }
}
