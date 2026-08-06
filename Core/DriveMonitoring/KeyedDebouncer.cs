namespace Lertaro.Core.DriveMonitoring;

// Coalesces repeated calls for the same key into a single delayed action, resetting the delay each time
// a new call for that key arrives -- e.g. many rapid filesystem-watcher events for one drive collapsing
// into a single expensive full-snapshot persist once that drive goes quiet for `delayMs`, instead of
// paying that cost once per raw event. Shared by WatcherManager (network/WSL/folder-index drives) and
// UsnIndexerExtensions.ApplyFolderChange (local drives without USN journal support), which both used to
// persist on every single change with no throttling at all.
internal sealed class KeyedDebouncer<TKey> : IDisposable where TKey : notnull
{
    private readonly Dictionary<TKey, Timer> _pending;
    private readonly object _gate = new();
    private readonly int _delayMs;

    public KeyedDebouncer(int delayMs, IEqualityComparer<TKey>? comparer = null)
    {
        _delayMs = delayMs;
        _pending = new Dictionary<TKey, Timer>(comparer);
    }

    public void Schedule(TKey key, Action action)
    {
        lock (_gate)
        {
            if (_pending.TryGetValue(key, out var existing))
                existing.Dispose();

            _pending[key] = new Timer(_ =>
            {
                lock (_gate)
                    _pending.Remove(key);
                action();
            }, null, _delayMs, Timeout.Infinite);
        }
    }

    // Drops a pending call without running it -- e.g. the drive is being removed/torn down, so whatever
    // was about to be persisted no longer matters.
    public void Cancel(TKey key)
    {
        lock (_gate)
        {
            if (_pending.Remove(key, out var timer))
                timer.Dispose();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            foreach (var timer in _pending.Values)
                timer.Dispose();
            _pending.Clear();
        }
    }
}
