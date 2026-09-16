namespace Lertaro.Core.Services.Search;

// Owns the shared per-directory live scans belonging to one SearchService, i.e. one search window.
//
// A scan deliberately outlives the query that started it: a later keystroke in the same directory reuses
// the finished list instead of re-walking the disk, so its lifetime cannot be tied to that query's own
// token. Something still has to own it, and this is that something. Each entry carries its own
// CancellationTokenSource linked to the owner's, which is what makes the guarantees below possible:
//
//   - Eviction drops the OLDEST entry without cancelling it. Cancelling was the earlier behaviour and was
//     wrong on two counts: a dropped scan can still be awaited by a request that has not finished (a
//     scoped search runs several folders at once), so cancelling it failed that request with an
//     OperationCanceledException for a reason nobody asked for; and the memory the cap reclaims is the
//     cached RESULT LIST, which an awaiting caller holds a reference to anyway, so dropping the entry
//     already releases everything the cap is for. See EvictLocked for the ceiling this leaves.
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
    private long _nextSequence;
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

            // ponytail: the per-scan linked CTS is never disposed and never cancelled on its own. It is
            // only ever created via CreateLinkedTokenSource -- no CancelAfter, so no timer and no unmanaged
            // handle -- and the owning cache's Dispose cancels it through the owner it is linked to, which
            // is also what keeps a scan from outliving its window. Add disposal here if a timer ever gets
            // attached to it.
            var cts = CancellationTokenSource.CreateLinkedTokenSource(_ownerCts.Token);
            var task = Task.Run(() => scan(directory, cts.Token), cts.Token);
            var entry = new LiveScan(task, ++_nextSequence);
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

    // Makes room by dropping the oldest entry -- oldest by when its scan STARTED, so the list a long-typing
    // session is most likely to reuse again is the one that survives.
    //
    // Deliberately not cancelled (see this class's header): a dropped scan may still be awaited by a request
    // that is running right now, and cancelling it would fail that request for a reason it never asked for.
    // Dropping the entry alone already releases what the cap is for -- the cached list -- because an
    // awaiting caller holds its own reference to the task that produces it.
    //
    // ponytail: a scan dropped while still walking therefore keeps walking to its own maxProcessed bound
    // with nothing able to reuse it, at most one per eviction. Typing is what drives evictions and a walk
    // costs a directory's worth of I/O, so this is bounded in practice; if walked-but-unwanted scans ever
    // show up as real CPU, track the number of awaiting callers per entry and cancel only unobserved ones.
    private void EvictLocked()
    {
        string? oldestKey = null;
        var oldestSequence = long.MaxValue;
        foreach (var (directory, entry) in _scans)
        {
            if (entry.Sequence < oldestSequence)
            {
                oldestSequence = entry.Sequence;
                oldestKey = directory;
            }
        }

        if (oldestKey != null)
            _scans.Remove(oldestKey);
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

    private sealed class LiveScan(Task<List<SearchResult>> task, long sequence)
    {
        internal Task<List<SearchResult>> Task { get; } = task;

        // Registration order, used only to pick the oldest entry to drop (see EvictLocked).
        internal long Sequence { get; } = sequence;
    }
}
