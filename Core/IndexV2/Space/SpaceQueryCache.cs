namespace Lertaro.Core.IndexV2.Space;

// Kept separate so the live tree traversal remains below the repository's per-file line limit.
internal sealed class SpaceQueryCache
{
    private readonly Dictionary<string, SpaceQueryResult> _entries = new(StringComparer.OrdinalIgnoreCase);
    private long _revision = -1;

    public bool TryGet(long revision, string key, out SpaceQueryResult result)
    {
        lock (_entries)
        {
            if (revision == _revision)
                return _entries.TryGetValue(key, out result);
            result = default;
            return false;
        }
    }

    public void Store(long revision, string key, SpaceQueryResult result)
    {
        lock (_entries)
        {
            if (_revision != revision || _entries.Count >= 64)
            {
                _entries.Clear();
                _revision = revision;
            }
            _entries[key] = result;
        }
    }
}
