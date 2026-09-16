namespace Lertaro.Core.Services.Search;

// Owns the shared per-directory live scans belonging to one SearchService, i.e. one search window.
//
// A scan deliberately outlives the query that started it: a later keystroke in the same directory reuses
// the finished list instead of re-walking the disk, so its lifetime cannot be tied to that query's own
// token. Something still has to own it, and this is that something. Each entry carries its own
// CancellationTokenSource linked to the owner's, which is what makes the guarantees below possible:
//
//   - Eviction cancels what it drops. Dropping the entry alone left the walk running to completion while
//     nothing could reuse it (the entry was gone) and nothing could stop it short of the window closing.
//   - Dispose cancels and then waits. Cancelling alone is not enough when callers dispose with `using`:
//     returning before the scans had observed cancellation let them keep walking -- and keep invoking the
//     per-request match callback they captured -- past the point their owner considered itself closed.
//   - A scan is registered under the same lock that can dispose the cache, and Dispose closes
//     registration, so a scan can never be started and then stranded with no owner.
internal sealed class LiveScanCache : IDisposable
{
    private const int DefaultMaxEntries = 32;

    // How long Dispose waits for cancelled scans to unwind before giving up on them. A scan checks its
    // token between entries, so it normally stops at once; the bound exists only for a scan blocked
    // inside a filesystem call on a hung share, where waiting forever would hang the caller instead.
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(2);

    private readonly Dictionary<string, LiveScan> _scans = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _ownerCts = new();
    private readonly object _gate = new();
    private readonly int _maxEntries;
    private bool _disposed;

    internal LiveScanCache(int maxEntries = DefaultMaxEntries)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxEntries, 1);
        _maxEntries = maxEntries;
    }

    internal int Count
    {
        get { lock (_gate) return _scans.Count; }
    }

    // `scan` runs on a thread-pool thread and is handed the token governing the scan's OWN lifetime --
    // not the calling request's, which is expected to be cancelled long before the scan finishes.
    //
    // Sharing is per directory, never per service: an earlier version serialized every live scan behind
    // one lock on the SearchService instance, so typing into a directory that needs a live scan (excluded
    // from the index, an unconfigured network drive, ...) queued every later keystroke's own attempt
    // behind whichever happened to go first -- none of which could even check their own cancellation token
    // until they got the lock.
    internal Task<List<SearchResult>> GetOrAdd(string directory, Func<string, CancellationToken, List<SearchResult>> scan)
    {
        lock (_gate)
        {
            // Past disposal there is no caller left to stream to, and anything started now would be
            // unreachable by the cancellation that already happened.
            if (_disposed)
                return Task.FromResult(new List<SearchResult>());

            if (_scans.TryGetValue(directory, out var existing))
                return existing.Task;

            if (_scans.Count >= _maxEntries)
                EvictLocked();

            var cts = CancellationTokenSource.CreateLinkedTokenSource(_ownerCts.Token);
            var task = Task.Run(() => scan(directory, cts.Token), cts.Token);
            var entry = new LiveScan(cts, task);
            _scans[directory] = entry;
            _ = task.ContinueWith(t => RemoveIfFaulted(directory, entry, t), CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            return task;
        }
    }

    // A faulted scan must not poison every later keystroke in the same directory -- the retry is exactly
    // what a later caller does after this.
    private void RemoveIfFaulted(string directory, LiveScan entry, Task<List<SearchResult>> task)
    {
        if (!task.IsFaulted)
            return;

        lock (_gate)
        {
            if (_scans.TryGetValue(directory, out var current) && ReferenceEquals(current, entry))
                _scans.Remove(directory);
        }
    }

    private void EvictLocked()
    {
        foreach (var entry in _scans.Values)
            entry.Cancel();
        _scans.Clear();
    }

    public void Dispose()
    {
        LiveScan[] pending;
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            _ownerCts.Cancel();
            pending = [.. _scans.Values];
            _scans.Clear();
        }

        WaitForScans(pending);
        _ownerCts.Dispose();
        GC.SuppressFinalize(this);
    }

    private static void WaitForScans(LiveScan[] pending)
    {
        var running = pending.Where(entry => !entry.Task.IsCompleted).Select(entry => entry.Task).ToArray();
        if (running.Length == 0)
            return;

        try
        {
            if (!Task.WaitAll(running, ShutdownTimeout))
                Logger.Log($"[LiveScanCache] {running.Length} live scan(s) still running {ShutdownTimeout.TotalSeconds:0.#}s after cancellation; abandoning the wait.", LogLevel.Warn);
        }
        catch (AggregateException)
        {
            // Whichever request started a faulted scan has already reported it.
        }
    }

    private sealed class LiveScan(CancellationTokenSource cts, Task<List<SearchResult>> task)
    {
        internal Task<List<SearchResult>> Task { get; } = task;

        internal void Cancel() => cts.Cancel();

        // ponytail: the per-scan CancellationTokenSource is never disposed. It is only ever created via
        // CreateLinkedTokenSource -- no CancelAfter, so no timer and no unmanaged handle -- and its
        // lifetime is bounded by the owning cache's own Dispose, which cancels it and releases the
        // registration it holds on the owner. Add disposal here if a timer ever gets attached to it.
    }
}
