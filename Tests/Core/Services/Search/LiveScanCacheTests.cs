using Lertaro.Core.Services.Search;

namespace Lertaro.Core.Tests.Services.Search;

// A live scan is shared between keystrokes and deliberately outlives any one of them, so its lifetime is
// owned by the window (the cache) rather than by the request that started it. These pin what that ownership
// has to mean: a scan is started once, eviction drops only the cached list (never cancelling a scan another
// request may still be awaiting), and a disposed cache does not return while its scans are still running.
[TestClass]
public sealed class LiveScanCacheTests
{
    private static List<SearchResult> Empty() => [];

    [TestMethod]
    public async Task GetOrAdd_SameDirectoryTwice_StartsTheScanOnce()
    {
        using var cache = new LiveScanCache();
        var starts = 0;

        var first = cache.GetOrAdd(@"C:\dir", (_, _) => { Interlocked.Increment(ref starts); return Empty(); });
        var second = cache.GetOrAdd(@"C:\dir", (_, _) => { Interlocked.Increment(ref starts); return Empty(); });

        await Task.WhenAll(first, second);

        Assert.AreEqual(1, starts);
        Assert.AreSame(first, second);
    }

    [TestMethod]
    public async Task GetOrAdd_DirectoryCaseInsensitive_IsTheSameScan()
    {
        using var cache = new LiveScanCache();
        var starts = 0;

        var first = cache.GetOrAdd(@"C:\Dir", (_, _) => { Interlocked.Increment(ref starts); return Empty(); });
        var second = cache.GetOrAdd(@"c:\dir", (_, _) => { Interlocked.Increment(ref starts); return Empty(); });

        await Task.WhenAll(first, second);

        Assert.AreEqual(1, starts);
    }

    [TestMethod]
    public async Task GetOrAdd_AtTheEntryLimit_DropsTheOldestEntryWithoutCancellingIt()
    {
        using var cache = new LiveScanCache(maxEntries: 2);
        var tokens = new List<CancellationToken>();

        for (var i = 0; i < 3; i++)
        {
            var index = i;
            await cache.GetOrAdd($@"C:\dir{index}", (_, token) => { tokens.Add(token); return Empty(); });
        }

        // The first entry was dropped to make room for the third -- but NOT cancelled. A dropped scan can
        // still be awaited by a request that has not finished (a scoped search fans out over several folders
        // at once), and cancelling it failed that request with an OperationCanceledException for a reason
        // nobody asked for. What the cap reclaims is the cached list, which dropping the entry already
        // releases because an awaiting caller holds its own reference to the task.
        Assert.IsFalse(tokens[0].IsCancellationRequested);
        Assert.IsFalse(tokens[1].IsCancellationRequested);
        Assert.IsFalse(tokens[2].IsCancellationRequested);
        Assert.AreEqual(2, cache.Count);
    }

    [TestMethod]
    public async Task GetOrAdd_AtTheEntryLimit_KeepsTheMostRecentDirectories()
    {
        using var cache = new LiveScanCache(maxEntries: 2);
        var starts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < 3; i++)
        {
            var directory = $@"C:\dir{i}";
            await cache.GetOrAdd(directory, (dir, _) =>
            {
                starts[dir] = starts.GetValueOrDefault(dir) + 1;
                return Empty();
            });
        }

        // The oldest entry is the one that goes: reading the list a long-typing session most recently reused
        // again is what the cache is for.
        await cache.GetOrAdd(@"C:\dir2", (dir, _) => { starts[dir] = starts.GetValueOrDefault(dir) + 1; return Empty(); });
        Assert.AreEqual(1, starts[@"C:\dir2"]);

        await cache.GetOrAdd(@"C:\dir0", (dir, _) => { starts[dir] = starts.GetValueOrDefault(dir) + 1; return Empty(); });
        Assert.AreEqual(2, starts[@"C:\dir0"], "the oldest entry must have been the one dropped");
    }

    [TestMethod]
    public async Task GetOrAdd_NeverExceedsTheEntryLimit()
    {
        using var cache = new LiveScanCache(maxEntries: 2);

        for (var i = 0; i < 5; i++)
        {
            var index = i;
            await cache.GetOrAdd($@"C:\dir{index}", (_, _) => Empty());
            Assert.IsLessThanOrEqualTo(2, cache.Count);
        }
    }

    [TestMethod]
    public void Dispose_CancelsOutstandingScans()
    {
        var cache = new LiveScanCache();
        var started = new ManualResetEventSlim();
        CancellationToken seen = default;

        var scan = cache.GetOrAdd(@"C:\dir", (_, token) =>
        {
            seen = token;
            started.Set();
            token.WaitHandle.WaitOne();
            return Empty();
        });
        started.Wait(TimeSpan.FromSeconds(5));

        cache.Dispose();

        Assert.IsTrue(seen.IsCancellationRequested);
        Assert.IsTrue(scan.IsCompleted);
    }

    [TestMethod]
    public void Dispose_DoesNotReturnWhileAScanIsStillRunning()
    {
        var cache = new LiveScanCache();
        var started = new ManualResetEventSlim();
        var finished = 0;

        cache.GetOrAdd(@"C:\dir", (_, token) =>
        {
            started.Set();
            token.WaitHandle.WaitOne();
            // Cancellation is observed immediately; this stands in for a scan still unwinding afterwards,
            // which is exactly the window Dispose has to cover. Without the wait the delay outlives it.
            Thread.Sleep(250);
            Interlocked.Exchange(ref finished, 1);
            return Empty();
        });
        started.Wait(TimeSpan.FromSeconds(5));

        cache.Dispose();

        // Returning before the scan observed cancellation is exactly what let it keep walking -- and keep
        // calling back into a request that had already moved on -- after its owner considered itself closed.
        Assert.AreEqual(1, Volatile.Read(ref finished));
    }

    [TestMethod]
    public void Dispose_IsIdempotent()
    {
        var cache = new LiveScanCache();

        cache.Dispose();
        cache.Dispose();
    }

    [TestMethod]
    public async Task GetOrAdd_AfterDispose_DoesNotStartAScan()
    {
        var cache = new LiveScanCache();
        cache.Dispose();
        var starts = 0;

        var scan = cache.GetOrAdd(@"C:\dir", (_, _) => { Interlocked.Increment(ref starts); return Empty(); });

        Assert.AreEqual(0, starts);
        Assert.IsEmpty(await scan);
        Assert.AreEqual(0, cache.Count);
    }

    [TestMethod]
    public async Task GetOrAdd_FaultedScan_IsDroppedSoTheNextCallRetries()
    {
        using var cache = new LiveScanCache();
        var starts = 0;

        var faulted = cache.GetOrAdd(@"C:\dir", (_, _) =>
        {
            Interlocked.Increment(ref starts);
            throw new IOException("scan failed");
        });
        await Assert.ThrowsAsync<IOException>(() => faulted);
        await WaitUntil(() => cache.Count == 0);

        // A faulted scan must not poison every later keystroke in the same directory.
        var retried = cache.GetOrAdd(@"C:\dir", (_, _) => { Interlocked.Increment(ref starts); return Empty(); });
        await retried;

        Assert.AreEqual(2, starts);
        Assert.AreEqual(1, cache.Count);
    }

    [TestMethod]
    public async Task GetOrAdd_FaultedScan_DoesNotAffectOtherDirectories()
    {
        using var cache = new LiveScanCache();

        var faulted = cache.GetOrAdd(@"C:\bad", (_, _) => throw new IOException("scan failed"));
        await Assert.ThrowsAsync<IOException>(() => faulted);

        var healthy = cache.GetOrAdd(@"C:\good", (_, _) => [new SearchResult { Name = "a.txt", Path = @"C:\good\a.txt" }]);

        Assert.HasCount(1, await healthy);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var i = 0; i < 500 && !condition(); i++)
            await Task.Delay(10);
        Assert.IsTrue(condition(), "condition was not reached in time");
    }
}
