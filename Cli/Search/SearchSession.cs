using System.Text;
using Lertaro.Core;

using Lertaro.Core.SearchIndex.Query;
namespace Lertaro.Cli.Search;

// Owns everything about "what's currently on screen and why": the live query text, the current result
// set, Tab-marked selections, and the debounced search-in-flight machinery. Raises Changed whenever
// something a renderer would need to redraw has changed -- callers (Program.cs) subscribe a Render()
// method to it rather than this class knowing anything about how results get drawn.
public sealed class SearchSession
{
    public const int MaxVisible = 15;
    private const int MaxResultsShown = 50;

    private readonly string _pipeName;

    public StringBuilder Query { get; } = new();
    public int CursorPos { get; set; } // edit position within Query, 0..Query.Length

    private readonly object _resultsLock = new();
    private List<(SearchResult Result, int[] Highlights)> _results = new();
    public IReadOnlyList<(SearchResult Result, int[] Highlights)> Results
    {
        get
        {
            lock (_resultsLock)
                return _results.ToArray();
        }
    }

    // Keyed by Path, not by index into Results -- Results gets entirely replaced by every new search (a
    // fresh List each time, not an in-place update), so an index-based selection would silently point at
    // whatever unrelated row happens to land on that same slot after the query changes. Path is the one
    // thing that identifies "the same result" across two otherwise-unrelated result sets, which is what
    // lets a Tab-marked row survive changing the query and (if it reappears later, e.g. the user retypes
    // the original query) still show marked. Never cleared on a new search -- see RunSearchAsync.
    private readonly HashSet<string> _selected = new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlySet<string> Selected => _selected;

    public int HighlightIndex { get; private set; }
    public int ViewOffset { get; private set; } // index of the first row currently drawn
    public string StatusLine { get; private set; } = "";

    private int _searchGeneration;
    private CancellationTokenSource? _searchCts;

    // Fired after any state change a renderer would need to redraw for -- typing, navigation, or a search
    // completing/streaming in a progress snapshot.
    public event Action? Changed;

    public SearchSession(string pipeName) => _pipeName = pipeName;

    // Test seam: _results otherwise only changes via RunSearchAsync's real App-pipe round trip, which
    // unit tests can't drive. Mirrors that method's own post-search reset (HighlightIndex/ViewOffset
    // both back to 0) so MoveHighlight/ToggleSelectionAtHighlight/GetChosenPaths can be exercised
    // against a known result set exactly as they'd see one after a real search completes.
    internal void SetResultsForTests(IReadOnlyList<(SearchResult Result, int[] Highlights)> results)
    {
        lock (_resultsLock)
        {
            _results = results.ToList();
            HighlightIndex = 0;
            ViewOffset = 0;
        }
    }

    // Moves the highlight by `delta` rows (±1 for arrow keys, ±MaxVisible for PageUp/PageDown), clamped to
    // the result set, then slides ViewOffset just enough to keep the new highlight inside the visible
    // window -- never re-centers or over-scrolls, matching how a normal list box/fzf's own viewport
    // behaves.
    public void MoveHighlight(int delta)
    {
        lock (_resultsLock)
        {
            if (_results.Count == 0) return;
            HighlightIndex = Math.Clamp(HighlightIndex + delta, 0, _results.Count - 1);
            if (HighlightIndex < ViewOffset)
                ViewOffset = HighlightIndex;
            else if (HighlightIndex >= ViewOffset + MaxVisible)
                ViewOffset = HighlightIndex - MaxVisible + 1;
            ViewOffset = Math.Clamp(ViewOffset, 0, Math.Max(0, _results.Count - MaxVisible));
        }
    }

    public void ToggleSelectionAtHighlight()
    {
        lock (_resultsLock)
        {
            if (_results.Count == 0) return;
            var path = _results[HighlightIndex].Result.Path;
            if (!_selected.Add(path)) _selected.Remove(path);
        }
        MoveHighlight(1);
    }

    // Enter's own output: every Tab-marked path if there are any (regardless of what's currently
    // highlighted), otherwise just whatever's highlighted right now.
    public IEnumerable<string> GetChosenPaths()
    {
        lock (_resultsLock)
        {
            if (_selected.Count > 0)
                return _selected.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray();
            return _results.Count > 0 ? new[] { _results[HighlightIndex].Result.Path } : Array.Empty<string>();
        }
    }

    // Shared by both the initial query (args[0]/stdin, if any) and every subsequent keystroke -- 80ms
    // debounce since every search here is a real App-pipe network round trip, not worth firing on every
    // single character.
    public void TriggerSearch()
    {
        Changed?.Invoke();
        var generation = ++_searchGeneration;
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        var cts = _searchCts = new CancellationTokenSource();
        var q = Query.ToString();
        _ = Task.Run(async () =>
        {
            try { await Task.Delay(80, cts.Token); }
            catch { return; }
            if (generation != _searchGeneration) return;
            await RunSearchAsync(q, generation, cts.Token);
        });
    }

    private async Task RunSearchAsync(string q, int generation, CancellationToken token)
    {
        var rankComparer = new SearchResultRankComparer(SearchHistoryStore.Snapshot());
        var comparer = Comparer<(SearchResult Result, int[] Highlights)>.Create((a, b) => rankComparer.Compare(a.Result, b.Result));
        // A trailing " :a,b,c" query-token suffix (same shared Core parser AppSearchPipeService itself
        // uses to decide this) means the server already ran the full rank-sort + IQueryTokenProvider
        // dispatch and sent back the finished, final-order result in one shot -- re-sorting here with a
        // plain rank comparer would scramble whatever a token like "::expr" deliberately reordered by.
        // Progress snapshots are also moot in that case (the server never streams partial results for a
        // tokenized query), so this only really changes the final callback's behavior.
        SearchQuerySortParser.Strip(q, out var parsedTokens);
        var hasTokens = parsedTokens.Count > 0;

        void ShowSnapshot(List<(SearchResult, int[])> snapshot)
        {
            if (generation != _searchGeneration) return;
            var sorted = new List<(SearchResult, int[])>(snapshot);
            if (!hasTokens) sorted.Sort(comparer);
            // Capped client-side, after ranking -- not by asking the App/SearchStreamingAsync for fewer
            // candidates. A smaller request-side limit shrinks the candidate pool the ranking runs over,
            // which is exactly what silently dropped a pinyin match once (a deep/rare candidate the scan
            // happens to reach late never got INTO the pool at all, however good its eventual rank would
            // have been). The App still scans/ranks its full depth; this just doesn't bother rendering
            // past the point a human would actually scroll to.
            lock (_resultsLock)
            {
                _results = sorted.Count > MaxResultsShown ? sorted.GetRange(0, MaxResultsShown) : sorted;
                StatusLine = "";
                // Deliberately does NOT reset selection/highlight/viewOffset mid-stream -- only the final
                // snapshot below does that. Resetting on every progress tick would fight the user if they
                // start navigating a promising early result before the search has fully settled.
                HighlightIndex = Math.Clamp(HighlightIndex, 0, Math.Max(0, _results.Count - 1));
                ViewOffset = Math.Clamp(ViewOffset, 0, Math.Max(0, _results.Count - MaxVisible));
            }
            Changed?.Invoke();
        }

        var fresh = new List<(SearchResult, int[])>();
        if (!string.IsNullOrWhiteSpace(q))
        {
            try
            {
                fresh = await AppSearchPipeClient.SearchAsync(_pipeName, q, ShowSnapshot, token);
            }
            catch (OperationCanceledException)
            {
                return; // superseded by a newer keystroke -- nothing to show, the newer search owns the screen now
            }
            catch (Exception ex)
            {
                if (generation == _searchGeneration)
                    StatusLine = $"[error] {ex.GetType().Name}: {ex.Message} -- is the Lertaro App running?";
            }
        }

        if (generation != _searchGeneration) return;

        if (!hasTokens) fresh.Sort(comparer);
        lock (_resultsLock)
        {
            _results = fresh.Count > MaxResultsShown ? fresh.GetRange(0, MaxResultsShown) : fresh;
            if (string.IsNullOrEmpty(StatusLine) || !StatusLine.StartsWith("[error]"))
                StatusLine = "";
            // Deliberately does NOT clear _selected -- it's keyed by Path precisely so a Tab-marked row from
            // an earlier query survives this reassignment even though it (usually) isn't part of Results
            // anymore.
            HighlightIndex = 0;
            ViewOffset = 0;
        }
        Changed?.Invoke();
    }
}
