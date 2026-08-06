using Lertaro.Core;

namespace Lertaro.App.ViewModels.Search.Mapping;

/// <summary>
/// Builds the full window's rows incrementally as a search streams: each call maps and ranks only the
/// results that have arrived since the last one, then merges them into the order already established.
/// </summary>
/// <remarks>
/// The mapper this replaces re-did everything on every paint -- rank-sort the whole snapshot, then
/// build a fresh AppSearchResult for every row of it. At roughly 2.4us per row that is affordable once
/// and nothing like affordable repeatedly, so painting had to be rationed, and rationing it is what
/// produced a list that climbed to a hundred thousand rows and then sat there while the rest of the
/// search finished.
///
/// Incremental removes the reason to ration. Every result is mapped exactly once no matter how many
/// paints it appears in, so the total cost of a fully progressive search is the cost of the single
/// pass it used to do at the end -- what changes is when that work happens, not how much of it there
/// is. Sorting gets cheaper too (k sorts of n/k beats one sort of n), and the merges that replace it
/// are linear passes over references, cheap enough next to construction to disappear into the noise.
///
/// One accumulator serves one query. The caller hands it the SAME growing list every time, always a
/// prefix-stable arrival-ordered snapshot, and it tracks how far into that list it has already read.
/// </remarks>
internal sealed class StreamingResultAccumulator
{
    private readonly record struct Entry(SearchResult Raw, AppSearchResult Row);

    private readonly string _query;
    private readonly string? _queriedDirectory;
    private readonly IComparer<SearchResult> _rankComparer;

    private int _consumed;
    private List<Entry> _ranked = new();
    // The merge writes into this and the two swap, so a search does not allocate a fresh full-size
    // list on every paint just to hold the same rows in a slightly different order.
    private List<Entry> _spare = new();
    private readonly List<AppSearchResult> _rows = new();

    public StreamingResultAccumulator(string query, IReadOnlyDictionary<string, int> historySnapshot)
    {
        _query = query;
        _queriedDirectory = SearchResultMapper.GetQueriedDirectory(query);
        // Captured once for the whole query rather than re-read per paint: the ranking must not shift
        // underneath rows that are already on screen just because the user opened something mid-search.
        _rankComparer = new SearchResultRankComparer(historySnapshot);
    }

    /// <summary>How many of the arrivals handed in so far have been mapped.</summary>
    public int Consumed => _consumed;

    /// <summary>Rows currently held, in rank order.</summary>
    public int Count => _ranked.Count;

    /// <summary>
    /// Index of the first row the most recent <see cref="Absorb"/> changed. Everything before it is
    /// untouched, which is what lets the view update only the tail instead of rebuilding.
    /// </summary>
    /// <remarks>
    /// A merge only disturbs the list from the position the best NEW entry lands at. Late in a search
    /// the arrivals are the dregs -- they rank below almost everything already shown -- so that
    /// position is near the end and the change is a handful of rows, even though the list is hundreds
    /// of thousands long. Reporting it turns a repaint from work proportional to the whole list into
    /// work proportional to what actually moved, which is the difference between a paint the list can
    /// afford several times a second and one it can afford every few minutes.
    /// </remarks>
    public int FirstChangedIndex { get; private set; }

    /// <summary>
    /// Absorbs everything in <paramref name="arrivals"/> past what previous calls already took, and
    /// returns the complete ranked row list. <paramref name="arrivals"/> must be arrival-ordered and
    /// must never rewrite the prefix a previous call read.
    /// </summary>
    /// <remarks>
    /// The SAME list instance comes back every time, updated in place -- a fresh one per paint would be
    /// a multi-megabyte large-object allocation several times a second for a big search. It is valid
    /// until the next call, which is all any synchronous consumer needs; the render pump waits for the
    /// UI to finish applying a paint before computing the next, so no consumer overlaps one. A caller
    /// that does keep it across an await (the query-token path) must copy it first.
    /// </remarks>
    public List<AppSearchResult> Absorb(IReadOnlyList<SearchResult> arrivals)
    {
        FirstChangedIndex = _ranked.Count;

        if (arrivals.Count > _consumed)
        {
            var chunk = new List<Entry>(arrivals.Count - _consumed);
            for (var i = _consumed; i < arrivals.Count; i++)
            {
                var raw = arrivals[i];
                // Typing an exact directory path is a request to look INSIDE it, so the directory's own
                // index record is not one of its own results (see SearchResultMapper).
                if (SearchResultMapper.IsQueriedDirectory(raw.Path, _queriedDirectory))
                    continue;
                chunk.Add(new Entry(raw, SearchResultMapper.CreateUiResult(raw, _query, 0, isApplication: false, scope: null)));
            }

            _consumed = arrivals.Count;
            chunk.Sort(CompareEntries);
            Merge(chunk);
        }

        // Only the disturbed suffix is rewritten. Index is restamped over the same range because a row
        // inserted in the middle shifts every position after it.
        while (_rows.Count < _ranked.Count)
            _rows.Add(null!);
        if (_rows.Count > _ranked.Count)
            _rows.RemoveRange(_ranked.Count, _rows.Count - _ranked.Count);

        for (var i = FirstChangedIndex; i < _ranked.Count; i++)
        {
            var row = _ranked[i].Row;
            row.Index = i;
            _rows[i] = row;
        }
        return _rows;
    }

    private int CompareEntries(Entry left, Entry right) => _rankComparer.Compare(left.Raw, right.Raw);

    private void Merge(List<Entry> chunk)
    {
        if (chunk.Count == 0)
            return;

        if (_ranked.Count == 0)
        {
            _ranked = chunk;
            FirstChangedIndex = 0;
            return;
        }

        var target = _spare;
        target.Clear();
        var total = _ranked.Count + chunk.Count;
        if (target.Capacity < total)
            target.Capacity = total;

        int a = 0, b = 0;
        while (a < _ranked.Count && b < chunk.Count)
        {
            if (CompareEntries(chunk[b], _ranked[a]) < 0)
            {
                // The first time a new entry wins is the first position that differs from the old list.
                if (target.Count < FirstChangedIndex)
                    FirstChangedIndex = target.Count;
                target.Add(chunk[b++]);
            }
            else
            {
                target.Add(_ranked[a++]);
            }
        }
        while (a < _ranked.Count)
            target.Add(_ranked[a++]);
        while (b < chunk.Count)
        {
            if (target.Count < FirstChangedIndex)
                FirstChangedIndex = target.Count;
            target.Add(chunk[b++]);
        }

        _spare = _ranked;
        _ranked = target;
    }
}
