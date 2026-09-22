namespace Lertaro.Core.DriveMonitoring;

// Coalesces repeated calls for the same key into a single delayed action, resetting the delay each time
// a new call for that key arrives -- e.g. many rapid filesystem-watcher events for one drive collapsing
// into a single expensive full-snapshot persist once that drive goes quiet for `delayMs`, instead of
// paying that cost once per raw event. Shared by WatcherManager (network/WSL/folder-index drives) and
// UsnIndexerExtensions.ApplyFolderChange (local drives without USN journal support), which both used to
// persist on every single change with no throttling at all.
internal sealed class KeyedDebouncer<TKey> : IDisposable where TKey : notnull
{
    // Each schedule carries a generation so a callback can tell whether the entry it is about to remove
    // is still its own. Timer.Dispose() (the parameterless overload) does not wait for a callback already
    // in flight, so without this a callback blocked on _gate while Schedule replaced it would remove the
    // replacement by key -- without disposing it, so the expensive action ran a second time when the
    // replacement fired -- and that replacement's own callback would then remove whatever entry existed at
    // the time, leaving a later Cancel with nothing to cancel.
    private readonly Dictionary<TKey, (Timer Timer, long Generation)> _pending;
    private readonly object _gate = new();
    private readonly int _delayMs;
    private long _generation;

    public KeyedDebouncer(int delayMs, IEqualityComparer<TKey>? comparer = null)
    {
        _delayMs = delayMs;
        _pending = new Dictionary<TKey, (Timer, long)>(comparer);
    }

    public void Schedule(TKey key, Action action)
    {
        lock (_gate)
        {
            if (_pending.TryGetValue(key, out var existing))
                existing.Timer.Dispose();

            var generation = ++_generation;
            var timer = new Timer(_ =>
            {
                lock (_gate)
                {
                    if (_pending.TryGetValue(key, out var live) && live.Generation == generation)
                        _pending.Remove(key);
                }

                action();
            }, null, _delayMs, Timeout.Infinite);
            _pending[key] = (timer, generation);
        }
    }

    // Drops a pending call without running it -- e.g. the drive is being removed/torn down, so whatever
    // was about to be persisted no longer matters.
    public void Cancel(TKey key)
    {
        lock (_gate)
        {
            if (_pending.Remove(key, out var entry))
                entry.Timer.Dispose();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            foreach (var entry in _pending.Values)
                entry.Timer.Dispose();
            _pending.Clear();
        }
    }
}
