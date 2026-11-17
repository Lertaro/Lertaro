using Lertaro.Core.IndexV2.Delta;
using Lertaro.Core.IndexV2.Persistence;
using Lertaro.Core.IndexV2.Search;

namespace Lertaro.Core.IndexV2.Space;

// One search snapshot can produce many directory rows. The delta-affected bookkeeping and any
// recursive additions are therefore prepared once, while unaffected snapshot directories answer from
// RecursiveSizes in O(1).
internal sealed class DirectorySizeResolver
{
    private readonly Snapshot _snapshot;
    private readonly DeltaOverlay _delta;
    private readonly DeltaChildLookup? _lookup;
    private readonly HashSet<int> _affected;
    private readonly Dictionary<UInt128, int> _canonicalAddedLinks;
    private readonly Dictionary<int, long> _memoizedAffected = new();

    public DirectorySizeResolver(Snapshot snapshot, DeltaOverlay delta)
    {
        _snapshot = snapshot;
        _delta = delta;
        _lookup = DeltaChildLookup.Build(snapshot, delta);
        _affected = LiveSpaceQuery.BuildAffectedEntries(snapshot, delta, out _canonicalAddedLinks);
    }

    public long GetRecursiveSize(int entry)
    {
        if (_memoizedAffected.TryGetValue(entry, out var cached))
            return cached;

        var size = Math.Max(0, LiveSpaceQuery.GetRecursiveSize(
            _snapshot, _delta, _lookup, _affected, _canonicalAddedLinks, entry));
        _memoizedAffected[entry] = size;
        return size;
    }
}
