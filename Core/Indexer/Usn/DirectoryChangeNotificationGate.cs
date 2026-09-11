namespace Lertaro.Core.Indexer.Usn;

// Holds directory-change notifications while startup replays cached journal records. The index
// remains searchable during that replay, but subscribers must not begin work until the replay has
// produced a stable post-catch-up view.
internal sealed class DirectoryChangeNotificationGate
{
    private readonly Action<string, IReadOnlyCollection<string>?> _publish;
    private readonly object _lock = new();
    private readonly Dictionary<string, HashSet<string>?> _pending = new(StringComparer.OrdinalIgnoreCase);
    private int _suspensionCount;

    public DirectoryChangeNotificationGate(Action<string, IReadOnlyCollection<string>?> publish) => _publish = publish;

    public IDisposable Begin()
    {
        lock (_lock)
            _suspensionCount++;
        return new Suspension(this);
    }

    public bool TryDefer(string drive, IReadOnlyCollection<string>? changedDirectories)
    {
        lock (_lock)
        {
            if (_suspensionCount == 0)
                return false;

            if (!_pending.TryGetValue(drive, out var pendingDirectories))
            {
                _pending[drive] = changedDirectories == null
                    ? null
                    : new HashSet<string>(changedDirectories, StringComparer.OrdinalIgnoreCase);
                return true;
            }

            if (pendingDirectories == null || changedDirectories == null)
                return true;

            pendingDirectories.UnionWith(changedDirectories);
            return true;
        }
    }

    private void End()
    {
        Dictionary<string, HashSet<string>?>? pending = null;
        lock (_lock)
        {
            _suspensionCount--;
            if (_suspensionCount == 0 && _pending.Count > 0)
            {
                pending = new Dictionary<string, HashSet<string>?>(_pending, StringComparer.OrdinalIgnoreCase);
                _pending.Clear();
            }
        }

        if (pending == null)
            return;

        foreach (var (drive, directories) in pending)
            _publish(drive, directories);
    }

    private sealed class Suspension : IDisposable
    {
        private DirectoryChangeNotificationGate? _owner;

        public Suspension(DirectoryChangeNotificationGate owner) => _owner = owner;

        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.End();
    }
}
