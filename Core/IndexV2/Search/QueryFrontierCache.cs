namespace Lertaro.Core.IndexV2.Search;

// Candidate frontier for a plain query that is being extended character by character (a -> ap -> ape).
// Every name matching the longer subsequence also matched the shorter prefix, so the previous phase-A
// result is a complete superset for the next one. Only the UID list is retained; matching and ranking are
// still recomputed for the longer query. Deliberately restricted to short, operator-free literal text and
// to the exact index revision, which keeps the superset proof local and avoids stale or grammar-dependent
// reuse.
internal sealed class QueryFrontierCache
{
    private const int MaxQueryLength = 64;

    private readonly object _gate = new();
    private string _query = string.Empty;
    private int[] _uids = [];
    private long _revision = -1;
    private long _sequence;
    private long _storedSequence;

    internal long NextSearchSequence() => Interlocked.Increment(ref _sequence);

    internal int[]? TryGetSuperset(string query, long revision)
    {
        lock (_gate)
        {
            return _revision == revision
                && IsPlainQuery(_query)
                && IsSafeExtension(_query, query)
                ? _uids
                : null;
        }
    }

    internal void Store(string query, long revision, long sequence, IReadOnlyList<UniqueMatch> hits)
    {
        if (!IsPlainQuery(query))
            return;

        var uids = new int[hits.Count];
        for (var i = 0; i < hits.Count; i++)
            uids[i] = hits[i].Uid;

        lock (_gate)
        {
            // A search started earlier may finish after a newer one. It must not replace the newer
            // frontier, regardless of query length. Revision changes are authoritative and always reset.
            if (_revision == revision && sequence < _storedSequence)
                return;

            _query = query;
            _uids = uids;
            _revision = revision;
            _storedSequence = sequence;
        }
    }

    internal static bool IsSafeExtension(string previous, string query) =>
        IsPlainQuery(previous)
        && IsPlainQuery(query)
        && query.Length > previous.Length
        && query.StartsWith(previous, StringComparison.Ordinal);

    private static bool IsPlainQuery(string query)
    {
        if (query.Length is 0 or > MaxQueryLength)
            return false;

        foreach (var ch in query)
        {
            if (char.IsLetterOrDigit(ch) || ch is '.' or '-' or '_')
                continue;
            return false;
        }
        return true;
    }
}
