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
/// One accumulator serves one query. A caller may hand <see cref="Absorb"/> the same growing,
/// prefix-stable list, or hand <see cref="AbsorbBatch"/> only the new arrival-ordered batch.
/// </remarks>
internal sealed class StreamingResultAccumulator
{
    private readonly record struct Entry(SearchResult Raw, AppSearchResult Row);

    private readonly string _query;
    private readonly Lazy<string?> _queriedDirectory;
    private readonly IComparer<SearchResult> _rankComparer;

    private int _consumed;
    private List<Entry> _ranked = new();
    private readonly List<AppSearchResult> _rows = new();
    private readonly List<AppSearchResult> _lastBatchRows = new();
    private readonly HashSet<string> _seedPaths = new(StringComparer.OrdinalIgnoreCase);

    // Rows a content-style provider contributed, held at the FRONT of _rows. They come from a different
    // source than the index and outrank it by this window's own rule, so they are never entered into
    // _ranked -- but they do have to live in the one list every paint hands back, because a paint that
    // composed them into a fresh list would be a multi-megabyte copy of a search that can hold six
    // hundred thousand rows.
    //
    // A QUEUE rather than a single hand-off, and guarded rather than volatile: a provider that streams
    // answers in batches, and a second batch arriving before the pump takes up the first must extend it,
    // not replace it. The pump is the only reader; the UI thread is the only writer.
    private readonly List<AppSearchResult> _pendingPrefix = new();
    private readonly object _prefixLock = new();
    private int _prefixCount;
    // Absolute position in _rows, so it carries the prefix's own width. See FirstChangedIndex.
    private int _firstChanged;

    public StreamingResultAccumulator(
        string query,
        IReadOnlyDictionary<string, int> historySnapshot,
        IReadOnlyList<SearchResultMapper.RankedCandidate>? seedCandidates = null,
        Func<string, string?>? resolveQueriedDirectory = null)
    {
        _query = query;
        // Constructed on input, consumed by the background mapper. Directory.Exists can block on a
        // remote path; even an empty content-prefix paint on the UI thread must not resolve it.
        _queriedDirectory = new Lazy<string?>(() => (resolveQueriedDirectory ?? SearchResultMapper.GetQueriedDirectory)(query));
        // Captured once for the whole query rather than re-read per paint: the ranking must not shift
        // underneath rows that are already on screen just because the user opened something mid-search.
        _rankComparer = new SearchResultRankComparer(historySnapshot);

        if (seedCandidates == null)
            return;

        foreach (var candidate in seedCandidates)
        {
            if (!_seedPaths.Add(NormalizePath(candidate.Result.FullPath)))
                continue;
            var raw = new SearchResult
            {
                Name = candidate.Result.Name,
                Path = candidate.Result.FullPath,
                IsDir = candidate.Result.IsDir,
                Drive = candidate.Result.Drive
            };
            _ranked.Add(new Entry(raw, candidate.Result));
        }
        _ranked.Sort(CompareEntries);
        for (var index = 0; index < _ranked.Count; index++)
        {
            _ranked[index].Row.Index = index;
            _rows.Add(_ranked[index].Row);
        }
    }

    /// <summary>How many of the arrivals handed in so far have been mapped.</summary>
    public int Consumed => _consumed;

    /// <summary>Rows currently held, in rank order.</summary>
    public int Count => _ranked.Count;

    /// <summary>Rows created by the most recent arrival batch, before it was merged into rank order.</summary>
    public IReadOnlyList<AppSearchResult> LastBatchRows => _lastBatchRows;

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
    public int FirstChangedIndex => _firstChanged;

    /// <summary>
    /// The one list <see cref="Absorb"/> hands back: the content prefix, then the ranked index matches.
    /// </summary>
    public List<AppSearchResult> Rows => _rows;

    /// <summary>How many leading rows came from a content provider rather than from the index.</summary>
    public int ContentPrefixCount => _prefixCount;

    /// <summary>
    /// Adds rows to the front block, to be taken up by the next <see cref="Absorb"/>. Callable from any
    /// thread while the search streams; batches already placed stay, later ones extend them.
    /// </summary>
    public void QueueContentPrefix(List<AppSearchResult> rows)
    {
        if (rows.Count == 0)
            return;
        lock (_prefixLock)
        {
            _pendingPrefix.AddRange(rows);
        }
    }

    private bool ApplyQueuedPrefix()
    {
        List<AppSearchResult> queued;
        lock (_prefixLock)
        {
            if (_pendingPrefix.Count == 0)
                return false;
            // Copied out rather than swapped: another batch can be queued while this one goes in.
            queued = new List<AppSearchResult>(_pendingPrefix);
            _pendingPrefix.Clear();
        }

        // Behind whatever prefix is already displayed, so a streamed answer grows the block instead of
        // reordering the rows the user is looking at. Stamped here because RewriteRows only ever walks the
        // ranked index matches.
        for (var i = 0; i < queued.Count; i++)
            queued[i].Index = _prefixCount + i;
        _rows.InsertRange(_prefixCount, queued);
        _prefixCount += queued.Count;
        return true;
    }

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
        var start = Math.Min(_consumed, arrivals.Count);
        return AbsorbRange(arrivals, start, arrivals.Count - start);
    }

    /// <summary>Absorbs a batch containing only arrivals not supplied by an earlier call.</summary>
    public List<AppSearchResult> AbsorbBatch(IReadOnlyList<SearchResult> arrivals) =>
        AbsorbRange(arrivals, 0, arrivals.Count);

    private List<AppSearchResult> AbsorbRange(IReadOnlyList<SearchResult> arrivals, int start, int count)
    {
        _lastBatchRows.Clear();
        // Taken up at the start of a paint rather than by the thread that queued it, so one thread ever
        // touches _rows, and so the rewrite below restamps every row's position for the wider prefix.
        var prefixLanded = ApplyQueuedPrefix();

        var firstChanged = _ranked.Count;

        if (count > 0)
        {
            var queriedDirectory = _queriedDirectory.Value;
            var chunk = new List<Entry>(count);
            var end = start + count;
            for (var i = start; i < end; i++)
            {
                var raw = arrivals[i];
                // Typing an exact directory path is a request to look INSIDE it, so the directory's own
                // index record is not one of its own results (see SearchResultMapper).
                if (SearchResultMapper.IsQueriedDirectory(raw.Path, queriedDirectory))
                    continue;
                if (_seedPaths.Contains(NormalizePath(raw.Path)))
                    continue;
                var row = SearchResultMapper.CreateUiResult(raw, _query, 0, isApplication: false, scope: null);
                _lastBatchRows.Add(row);
                chunk.Add(new Entry(raw, row));
            }

            _consumed += count;
            chunk.Sort(CompareEntries);
            firstChanged = Merge(chunk);
        }

        // A prepend moves every row that was already there, so nothing on screen can be trusted to still
        // be in place and this paint compares from the top. Otherwise the merge's own position is where the
        // view can start, offset by the prefix that sits in front of it.
        RewriteRows(prefixLanded ? 0 : firstChanged);
        _firstChanged = prefixLanded ? 0 : _prefixCount + firstChanged;
        return _rows;
    }

    // Only the disturbed suffix is rewritten. Index is restamped over the same range because a row
    // inserted in the middle shifts every position after it.
    private void RewriteRows(int fromRelative)
    {
        var total = _prefixCount + _ranked.Count;
        while (_rows.Count < total)
            _rows.Add(null!);
        if (_rows.Count > total)
            _rows.RemoveRange(total, _rows.Count - total);

        for (var i = fromRelative; i < _ranked.Count; i++)
        {
            var row = _ranked[i].Row;
            // The display position, prefix included -- what it was before a prefix existed at all.
            row.Index = _prefixCount + i;
            _rows[row.Index] = row;
        }
    }

    private int CompareEntries(Entry left, Entry right) => _rankComparer.Compare(left.Raw, right.Raw);

    private static string NormalizePath(string path) =>
        path.Length > 3 && path[^1] == '\\' ? path.TrimEnd('\\') : path;

    /// <summary>Merges a sorted batch into the ranked list and returns the position it first disturbed.</summary>
    private int Merge(List<Entry> chunk)
    {
        if (chunk.Count == 0)
            return _ranked.Count;

        if (_ranked.Count == 0)
        {
            _ranked = chunk;
            return 0;
        }

        var oldCount = _ranked.Count;
        var firstChanged = FindInsertionPoint(chunk[0]);

        // Merge backward into the existing buffer. The untouched prefix is neither compared nor
        // copied, so a late batch that ranks near the end costs only its disturbed tail. AddRange is
        // also the complete fast path when the batch follows every existing row.
        _ranked.AddRange(chunk);
        var oldIndex = oldCount - 1;
        var chunkIndex = chunk.Count - 1;
        var writeIndex = _ranked.Count - 1;
        while (oldIndex >= firstChanged && chunkIndex >= 0)
        {
            if (CompareEntries(chunk[chunkIndex], _ranked[oldIndex]) >= 0)
                _ranked[writeIndex--] = chunk[chunkIndex--];
            else
                _ranked[writeIndex--] = _ranked[oldIndex--];
        }
        while (chunkIndex >= 0)
            _ranked[writeIndex--] = chunk[chunkIndex--];

        return firstChanged;
    }

    private int FindInsertionPoint(Entry entry)
    {
        var low = 0;
        var high = _ranked.Count;
        while (low < high)
        {
            var middle = low + (high - low) / 2;
            if (CompareEntries(entry, _ranked[middle]) < 0)
                high = middle;
            else
                low = middle + 1;
        }
        return low;
    }
}
