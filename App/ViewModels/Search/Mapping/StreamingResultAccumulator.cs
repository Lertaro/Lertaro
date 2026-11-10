using Lertaro.Core;
using Lertaro.Core.SearchIndex;

namespace Lertaro.App.ViewModels.Search.Mapping;

/// <summary>
/// Maps and ranks only newly arrived full-window results, then merges them into the existing order. Each
/// row is constructed once per query, avoiding repeated whole-snapshot mapping during progressive
/// rendering.
///
/// Absorb accepts a growing prefix-stable list; AbsorbBatch accepts only the next arrival-ordered batch.
/// </summary>
internal sealed class StreamingResultAccumulator
{
    internal const int RegexPathResultLimit = 8000;
    internal bool IsRegexPath { get; }
    private readonly record struct Entry(SearchResult Raw, AppSearchResult Row);

    private readonly string _query;
    private readonly Lazy<string?> _queriedDirectory;
    // Decided on the render callback before the first batch, then read-only: the rows already merged are
    // never re-ranked, so a late-arriving context cannot move what the user is looking at.
    private RankContext? _rankContext;

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
        IReadOnlyList<SearchResultMapper.RankedCandidate>? seedCandidates = null,
        Func<string, string?>? resolveQueriedDirectory = null)
    {
        _query = query;
        var ordinary = Core.SearchIndex.Query.RegexQueryParser.Split(query, out var regexes);
        IsRegexPath = regexes is { Length: > 0 } && Core.SearchIndex.Query.SearchQueryParser.Parse(ordinary).IsPathMode;
        // Constructed on input, consumed by the background mapper. Directory.Exists can block on a
        // remote path; even an empty content-prefix paint on the UI thread must not resolve it.
        _queriedDirectory = new Lazy<string?>(() => (resolveQueriedDirectory ?? SearchResultMapper.GetQueriedDirectory)(query));

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
            // No context yet -- UseRankContext re-decorates these rows before the first batch lands.
            SearchResultRanking.Decorate(raw, null);
            _ranked.Add(new Entry(raw, candidate.Result));
        }
        _ranked.Sort(CompareEntries);
        for (var index = 0; index < _ranked.Count; index++)
        {
            _ranked[index].Row.Index = index;
            _rows.Add(_ranked[index].Row);
        }
    }

    /// <summary>
    /// Hands the accumulator the ranking context (index-rule verdicts, decayed history scores, favorites)
    /// for this query. Only takes effect before the first file batch: once rows are on screen, re-ranking
    /// them would shift them under the user.
    /// </summary>
    /// <remarks>
    /// Separate from the constructor because the full window builds its accumulator on the UI thread, while
    /// working the signals out reads the history store and the favorites -- work that belongs on the render
    /// callback's background thread, ahead of its first batch. Seed rows have no batch behind them yet, so
    /// re-decorating and re-sorting them here is invisible to the user.
    /// </remarks>
    public void UseRankContext(RankContext? context)
    {
        if (_consumed != 0)
            return;

        _rankContext = context;
        if (_ranked.Count == 0)
            return;

        foreach (var entry in _ranked)
            SearchResultRanking.Decorate(entry.Raw, context);
        _ranked.Sort(CompareEntries);
        RewriteRows(0);
        _firstChanged = 0;
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
    /// A stable list of the content prefix followed by the ranked index matches.
    /// </summary>
    public List<AppSearchResult> Rows => new(_rows);

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
    /// The merge buffer stays on the producer thread. Published lists copy references, so a user can
    /// filter, sort or clear the previous answer while the next batch is being merged. Row identities
    /// are retained for reconciliation; the list's membership and order remain stable.
    /// </remarks>
    public List<AppSearchResult> Absorb(IReadOnlyList<SearchResult> arrivals, CancellationToken token = default)
    {
        var start = Math.Min(_consumed, arrivals.Count);
        return AbsorbRange(arrivals, start, arrivals.Count - start, token);
    }

    /// <summary>Absorbs a batch containing only arrivals not supplied by an earlier call.</summary>
    public List<AppSearchResult> AbsorbBatch(IReadOnlyList<SearchResult> arrivals, CancellationToken token = default) =>
        AbsorbRange(arrivals, 0, arrivals.Count, token);

    private List<AppSearchResult> AbsorbRange(IReadOnlyList<SearchResult> arrivals, int start, int count, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        _lastBatchRows.Clear();
        // Taken up at the start of a paint rather than by the thread that queued it, so one thread ever
        // touches _rows, and so the rewrite below restamps every row's position for the wider prefix.
        var prefixLanded = ApplyQueuedPrefix();

        var firstChanged = _ranked.Count;

        if (count > 0)
        {
            var buildStart = SearchPerf.Start();
            var queriedDirectory = _queriedDirectory.Value;
            var chunk = new List<Entry>(count);
            var end = start + count;
            for (var i = start; i < end; i++)
            {
                token.ThrowIfCancellationRequested();
                var raw = arrivals[i];
                // Typing an exact directory path is a request to look INSIDE it, so the directory's own
                // index record is not one of its own results (see SearchResultMapper).
                if (SearchResultMapper.IsQueriedDirectory(raw.Path, queriedDirectory))
                    continue;
                if (_seedPaths.Contains(NormalizePath(raw.Path)))
                    continue;
                // Decorated once, on arrival, so the section and score of a row are final before it is
                // merged in: the merge and every later paint only read those fields.
                SearchResultRanking.Decorate(raw, _rankContext);
                var row = SearchResultMapper.CreateUiResult(raw, _query, 0, isApplication: false, scope: null);
                _lastBatchRows.Add(row);
                chunk.Add(new Entry(raw, row));
            }
            var buildMs = SearchPerf.Ms(buildStart);

            _consumed += count;
            var sortStart = SearchPerf.Start();
            SortChunk(chunk, token);
            var sortMs = SearchPerf.Ms(sortStart);
            var mergeStart = SearchPerf.Start();
            firstChanged = Merge(chunk, token);
            var mergeMs = SearchPerf.Ms(mergeStart);

            // A prepend moves every row that was already there, so nothing on screen can be trusted to still
            // be in place and this paint compares from the top. Otherwise the merge's own position is where the
            // view can start, offset by the prefix that sits in front of it.
            var rewriteStart = SearchPerf.Start();
            RewriteRows(prefixLanded ? 0 : firstChanged, token);
            _firstChanged = prefixLanded ? 0 : _prefixCount + firstChanged;
            SearchPerf.Log($"[absorb] in={count} kept={chunk.Count} total={_ranked.Count} build={buildMs:F0}ms sort={sortMs:F0}ms merge={mergeMs:F0}ms rewrite={SearchPerf.Ms(rewriteStart):F0}ms firstChanged={firstChanged}");
            return new(_rows);
        }

        RewriteRows(prefixLanded ? 0 : firstChanged, token);
        _firstChanged = prefixLanded ? 0 : _prefixCount + firstChanged;
        return new(_rows);
    }

    // Only the disturbed suffix is rewritten. Index is restamped over the same range because a row
    // inserted in the middle shifts every position after it.
    private void RewriteRows(int fromRelative, CancellationToken token = default)
    {
        var total = _prefixCount + _ranked.Count;
        while (_rows.Count < total)
            _rows.Add(null!);
        if (_rows.Count > total)
            _rows.RemoveRange(total, _rows.Count - total);

        for (var i = fromRelative; i < _ranked.Count; i++)
        {
            token.ThrowIfCancellationRequested();
            var row = _ranked[i].Row;
            // The display position, prefix included -- what it was before a prefix existed at all.
            row.Index = _prefixCount + i;
            _rows[row.Index] = row;
        }
    }

    private static int CompareEntries(Entry left, Entry right) =>
        RankedSearchResultComparer.Instance.Compare(left.Raw, right.Raw);

    // The full window's last batch is the whole result set (the progressive cap keeps the streamed paints
    // small), and sorting it dominated the search: 913k rows cost 6.5s on one thread. Ordering is a total
    // order over independent rows, so sorting fixed-size blocks in parallel and merging them produces the
    // same list with the same comparison count spread over the cores. Small batches stay on the plain path,
    // where the split costs more than it saves.
    private const int ParallelSortThreshold = 20_000;
    private const int MaxSortParts = 8;

    private static void SortChunk(List<Entry> chunk, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var parts = Math.Min(MaxSortParts, chunk.Count / ParallelSortThreshold);
        if (parts < 2)
        {
            chunk.Sort(CompareEntries);
            token.ThrowIfCancellationRequested();
            return;
        }

        var partSize = (chunk.Count + parts - 1) / parts;
        var sorted = new List<Entry>[parts];
        Parallel.For(0, parts, new ParallelOptions { CancellationToken = token }, index =>
        {
            var start = index * partSize;
            var end = Math.Min(start + partSize, chunk.Count);
            var slice = chunk.GetRange(start, end - start);
            slice.Sort(CompareEntries);
            token.ThrowIfCancellationRequested();
            sorted[index] = slice;
        });

        var merged = MergeSorted(sorted, token);
        chunk.Clear();
        chunk.AddRange(merged);
    }

    private static List<Entry> MergeSorted(List<Entry>[] parts, CancellationToken token)
    {
        var level = new List<List<Entry>>(parts);
        while (level.Count > 1)
        {
            var next = new List<List<Entry>>((level.Count + 1) / 2);
            for (var i = 0; i < level.Count; i += 2)
                next.Add(i + 1 < level.Count ? Merge(level[i], level[i + 1], token) : level[i]);
            level = next;
        }

        return level[0];
    }

    private static List<Entry> Merge(List<Entry> left, List<Entry> right, CancellationToken token)
    {
        var merged = new List<Entry>(left.Count + right.Count);
        var i = 0;
        var j = 0;
        while (i < left.Count && j < right.Count)
        {
            token.ThrowIfCancellationRequested();
            merged.Add(CompareEntries(right[j], left[i]) < 0 ? right[j++] : left[i++]);
        }
        token.ThrowIfCancellationRequested();
        while (i < left.Count)
            merged.Add(left[i++]);
        while (j < right.Count)
            merged.Add(right[j++]);
        return merged;
    }

    private static string NormalizePath(string path) =>
        path.Length > 3 && path[^1] == '\\' ? path.TrimEnd('\\') : path;

    /// <summary>Merges a sorted batch into the ranked list and returns the position it first disturbed.</summary>
    private int Merge(List<Entry> chunk, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
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
            token.ThrowIfCancellationRequested();
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
