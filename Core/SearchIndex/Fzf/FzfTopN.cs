namespace Lertaro.Core.SearchIndex.Fzf;

// Keeps the `capacity` best (smallest sort key) of an unbounded stream of ranks.
//
// Buffered rather than kept exactly at capacity: the buffer grows with actual matches up to twice what
// is retained, and is trimmed back down to the best half only when it fills. Between trims an incoming
// rank is one compare against the threshold from the last trim, and a trim costs O(capacity) once per
// capacity accepted ranks, so the amortised cost per rank is constant. Growing from a modest initial
// buffer matters when an unbounded full-window query runs across several indexes concurrently: each
// index used to reserve arrays for every row before knowing how many names actually matched.
//
// It used to hold exactly capacity entries and, whenever one was displaced, rescan all of them to find
// the new worst -- O(capacity) per accepted rank rather than per capacity of them. A broad query
// accepts a large fraction of what it scans, so this was the dominant cost of the whole search: a
// single-character query over 400k rows spent more time here than in matching.
//
// Ties in sort key are common (every row of one unique name shares its key), and which of them survives
// at the capacity boundary is arbitrary -- as it was before. It does not reach the user: callers keep a
// capacity many times the number of results they display, and Finish sorts by EntryIndex within a key.
internal sealed class FzfTopN
{
    private const int InitialBufferedCapacity = 4_096;

    private readonly int _capacity;
    private readonly int _maximumBufferedLength;
    private ulong[] _sortKeys;
    private int[] _entryIndices;
    private int[] _scores;
    private int _count;

    // Only meaningful once the buffer has been trimmed at least once: the worst key then retained.
    // Matches the old comparison exactly, including that an equal key is rejected rather than kept.
    private ulong _threshold;
    private bool _trimmed;

    public FzfTopN(int capacity)
    {
        _capacity = Math.Max(capacity, 1);
        _maximumBufferedLength = _capacity > int.MaxValue / 2 ? int.MaxValue : _capacity * 2;
        var initialLength = Math.Min(_maximumBufferedLength, InitialBufferedCapacity);
        _sortKeys = new ulong[initialLength];
        _entryIndices = new int[initialLength];
        _scores = new int[initialLength];
    }

    /// <summary>How many ranks would be retained if the set were finished now.</summary>
    public int Count => Math.Min(_count, _capacity);

    public int Capacity => _capacity;

    // Lets an instance (and its arrays) be pooled across searches instead of re-allocated per query --
    // entries above _count are never read, so no clearing is needed.
    public void Reset()
    {
        _count = 0;
        _trimmed = false;
        _threshold = 0;
    }

    public void Add(FzfRank rank)
    {
        if (_trimmed && rank.SortKey >= _threshold)
            return;

        if (_count == _sortKeys.Length)
        {
            if (_sortKeys.Length < _maximumBufferedLength)
                Grow();
            else
                Trim();
        }

        _sortKeys[_count] = rank.SortKey;
        _entryIndices[_count] = rank.EntryIndex;
        _scores[_count] = rank.Score;
        _count++;

        if (_count == _maximumBufferedLength)
            Trim();
    }

    private void Grow()
    {
        var nextLength = (int)Math.Min(_maximumBufferedLength, (long)_sortKeys.Length * 2);
        Array.Resize(ref _sortKeys, nextLength);
        Array.Resize(ref _entryIndices, nextLength);
        Array.Resize(ref _scores, nextLength);
    }

    // Merges this instance's retained entries into another -- lets parallel workers keep private,
    // bounded sets and fold them together at the end instead of contending on one shared set.
    public void DrainInto(FzfTopN other)
    {
        TrimIfOverCapacity();
        for (var i = 0; i < _count; i++)
            other.Add(new FzfRank(_entryIndices[i], _scores[i], _sortKeys[i]));
    }

    public List<FzfRank> Finish(int limit)
    {
        TrimIfOverCapacity();
        var list = new List<FzfRank>(_count);
        for (var i = 0; i < _count; i++)
            list.Add(new FzfRank(_entryIndices[i], _scores[i], _sortKeys[i]));

        FzfRankRadixSorter.Sort(list);
        if (list.Count > limit)
            list.RemoveRange(limit, list.Count - limit);
        return list;
    }

    private void TrimIfOverCapacity()
    {
        if (_count > _capacity)
            Trim();
    }

    /// <summary>Moves the best <see cref="_capacity"/> entries to the front and drops the rest.</summary>
    private void Trim()
    {
        SelectSmallest(_capacity, _count);
        _count = _capacity;

        var worst = _sortKeys[0];
        for (var i = 1; i < _capacity; i++)
        {
            if (_sortKeys[i] > worst)
                worst = _sortKeys[i];
        }
        _threshold = worst;
        _trimmed = true;
    }

    /// <summary>
    /// Partitions so that the <paramref name="k"/> smallest sort keys occupy [0, k), in no particular
    /// order among themselves. Quickselect rather than a sort: the order inside the retained half is
    /// irrelevant here because Finish sorts anyway, and sorting the whole buffer on every trim would
    /// put back a log factor this exists to remove.
    /// </summary>
    /// <remarks>
    /// The partition is three-way, which here is not a refinement but the difference between linear and
    /// quadratic. A sort key is computed per unique NAME, so every row sharing a name shares its key and
    /// duplicates are the rule rather than the exception. A two-way partition sends every key equal to
    /// the pivot to the same side, so an all-equal run advances the bound by one element per pass:
    /// measured at 1.6 million comparisons for a single 3200-element trim, against the 3200 it should
    /// cost. Collecting the equal keys in the middle lets the search skip all of them at once.
    /// </remarks>
    private void SelectSmallest(int k, int length)
    {
        var low = 0;
        var high = length - 1;
        while (low < high)
        {
            var (lessEnd, equalEnd) = PartitionThreeWay(low, high);
            if (k <= lessEnd)
                high = lessEnd - 1;          // the k smallest are all below the pivot
            else if (k <= equalEnd + 1)
                return;                      // [0, k) is filled by keys <= the pivot: settled
            else
                low = equalEnd + 1;
        }
    }

    /// <summary>
    /// Splits [low, high] into keys below the pivot, equal to it, and above it, and returns where the
    /// first two end: [low, lessEnd) below, [lessEnd, equalEnd] equal, (equalEnd, high] above.
    /// </summary>
    private (int LessEnd, int EqualEnd) PartitionThreeWay(int low, int high)
    {
        // Median of three. Sort keys arrive in scan order, which correlates with the underlying name
        // table's order and so is far from random; a fixed pivot degrades badly on that.
        var mid = low + ((high - low) >> 1);
        if (_sortKeys[mid] < _sortKeys[low]) Swap(mid, low);
        if (_sortKeys[high] < _sortKeys[low]) Swap(high, low);
        if (_sortKeys[high] < _sortKeys[mid]) Swap(high, mid);
        var pivot = _sortKeys[mid];

        var less = low;
        var i = low;
        var greater = high;
        while (i <= greater)
        {
            var key = _sortKeys[i];
            if (key < pivot)
                Swap(i++, less++);
            else if (key > pivot)
                Swap(i, greater--);
            else
                i++;
        }
        return (less, greater);
    }

    private void Swap(int a, int b)
    {
        if (a == b)
            return;
        (_sortKeys[a], _sortKeys[b]) = (_sortKeys[b], _sortKeys[a]);
        (_entryIndices[a], _entryIndices[b]) = (_entryIndices[b], _entryIndices[a]);
        (_scores[a], _scores[b]) = (_scores[b], _scores[a]);
    }
}
