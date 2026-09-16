using System.IO;
using System.IO.Pipes;
using Lertaro.Core;
using Lertaro.App.ViewModels.Search;

using Lertaro.Core.Services.Search;
using Lertaro.Core.Wire;
using Lertaro.Core.SearchIndex;
using Lertaro.Core.SearchIndex.Query;
using Lertaro.App.ViewModels.Search.Mapping;
namespace Lertaro.App.Services.Pipe;

// Prototype: lets an external client (e.g. a CLI) reuse the App's own already-initialized search state
// -- AliasProviderRegistry's loaded plugins, UserNetworkDriveSearch's configured network/WSL/folder
// indexes -- instead of replicating that initialization itself. A bare client talking directly to the
// elevated service's own LertaroPipe only gets local NTFS/ReFS drives for free; anything routed
// through UserNetworkDriveSearch runs client-side and needs the same init the App already did at its
// own startup. Reuses the exact wire format LertaroPipe's own Search request already uses
// (SearchRequestBinarySerializer/SearchResponseBinarySerializer), so a client's read/write code is
// identical either way -- only the pipe name differs.
public static class AppSearchPipeService
{
    private static readonly SearchService SharedSearchService = new();

    public static void StopServer() => AppSearchPipeListener.Stop();

    public static Task StartPipeServerAsync() => Task.Run(() => AppSearchPipeListener.ListenLoopAsync(HandleClientAsync));

    private static async Task HandleClientAsync(NamedPipeServerStream pipe)
    {
        using (pipe)
        {
            try
            {
                while (pipe.IsConnected)
                {
                    // Bounded read: a stalled or silent client is dropped rather than parking this handler
                    // (and its connection slot) indefinitely. The timeout is reported as cancellation, so
                    // the catch below ends the connection quietly, as it does for any other disconnect.
                    using var readCts = new CancellationTokenSource(AppSearchPipeListener.RequestReadTimeout);
                    var request = await SearchRequestBinarySerializer.ReadSearchRequestAsync(pipe, readCts.Token);
                    if (request.Id == SearchRequestId.GetSpaceEntries)
                    {
                        await AppSearchPipeSpaceEntries.WriteAsync(SharedSearchService, request.Drive, pipe);
                        continue;
                    }

                    if (request.Id is not (SearchRequestId.Search or SearchRequestId.SearchDir))
                    {
                        await PipeResponseBinarySerializer.WriteErrorAsync(pipe, "Unsupported App search pipe request.");
                        continue;
                    }

                    using var queryCts = new CancellationTokenSource();
                    using var watchdogStopCts = new CancellationTokenSource();
                    _ = PipeDisconnectWatcher.WatchAsync(pipe, queryCts, watchdogStopCts.Token);
                    IdleWorkingSetTrimmer.BackgroundSearchStarted();
                    try
                    {
                        await RunFullWindowSearchAsync(request.Query ?? string.Empty,
                            request.Id == SearchRequestId.SearchDir ? request.DirectoryFilter : null,
                            pipe, queryCts.Token);
                    }
                    finally
                    {
                        watchdogStopCts.Cancel();
                        IdleWorkingSetTrimmer.BackgroundSearchFinished();
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"[AppSearchPipeService] Client handling ended: {ex.Message}", LogLevel.Debug);
            }
        }
    }

    // Mirrors SearchQueryDispatchController.OnAdvancedQueryChanged in full, including the part an
    // earlier version of this method skipped: query tokens ("\audio", "<s>20m") aren't part of the fuzzy
    // search text at all -- they're dispatched, AFTER the file search completes, to whichever
    // IQueryTokenProvider plugin claims each token, which can filter or reorder the already-ranked
    // results. Passing the raw (unstripped) query straight into SearchStreamingAsync -- what this used to
    // do -- searched for the literal token text instead of treating it as an operator, which is why that
    // syntax silently did nothing here.
    // Every result used to be its own write straight onto the pipe. That is a syscall each, and a
    // whole-drive query returns hundreds of thousands of them -- the same shape, on the GUI's own pipe,
    // measured 30us a result against 2.1 once the bytes were batched. Buffered here with the flush
    // policy SearchStreamPump already uses on the elevated service's pipe: the first ten results go out
    // immediately so a client sees something at once, then every fiftieth, then whatever is left at the
    // end. Without those flushes a short search would sit in the buffer until the End frame, which for a
    // CLI reading progressively is the difference between streaming and not.
    private const int WriteBufferSize = 8192;
    private const int FlushEveryResults = 50;
    private const int FlushEveryResultUntil = 10;

    private static async Task RunFullWindowSearchAsync(string query, string? directoryFilter, Stream pipe, CancellationToken token)
    {
        // Deliberately not disposed: disposing a BufferedStream closes what it wraps, and HandleClientAsync
        // reads the NEXT request off this same pipe when this returns. Everything written is flushed
        // explicitly below instead, so nothing is left in the buffer for a dispose to have to push out.
        var buffered = new BufferedStream(pipe, WriteBufferSize);
        await SearchResultWithHighlightBinarySerializer.WriteHeaderAsync(buffered, token);
        await buffered.FlushAsync(token);

        if (!string.IsNullOrWhiteSpace(query))
        {
            var scan = QueryTokenScanner.Scan(query, GetGlobalTokenPrefixChar());
            var cleanQuery = QueryTokenScanner.StripExclusionBypass(scan.Text, out var bypassExclusions);
            var tokens = scan.Tokens;

            if (tokens.Count > 0)
                await RunTokenizedSearchAsync(cleanQuery, tokens, directoryFilter, bypassExclusions, buffered, token);
            else
                await RunStreamingSearchAsync(cleanQuery, directoryFilter, bypassExclusions, buffered, token);
        }

        await SearchResultWithHighlightBinarySerializer.WriteEndAsync(buffered, token);
        await buffered.FlushAsync(token);
    }

    // The plain (no token) path: forwards each result to the pipe the instant SearchStreamingAsync
    // produces it, unsorted -- NOT accumulate-then-sort-then-send. That accumulate-first approach used
    // to mean the client saw nothing until BOTH the local (pipe) and network (in-process) sources had
    // fully finished, which felt much slower than the GUI's own full window (which renders progressively
    // as results stream in). Ranking (SearchResultRankComparer) is left to the client for the same
    // reason: it needs to re-run repeatedly against a growing snapshot, which belongs wherever the
    // incremental rendering is happening.
    private static async Task RunStreamingSearchAsync(string query, string? directoryFilter, bool bypassExclusions, Stream pipe, CancellationToken token)
    {
        // SearchStreamingAsync's onResult callback fires from whichever of its local/network tasks
        // produces a match, concurrently -- serialize pipe writes so two results' bytes never interleave
        // on the wire. The highlight mask is computed here (not by the client) for the same reason the
        // rest of this pipe exists: FuzzyMatcher.ComputeHighlightMask needs AliasProviderRegistry's
        // loaded plugins to correctly mark which characters of a pinyin/alias-matched CJK name matched,
        // which a bare client process has no way to reproduce.
        var writeLock = new SemaphoreSlim(1, 1);
        var written = 0;
        await SharedSearchService.SearchStreamingAsync(
            query,
            SearchViewModel.FullSearchFileLimit,
            SearchViewModel.FullSearchAppLimit,
            directoryFilter,
            r =>
            {
                if (SearchResultMapper.IsQueriedDirectoryItself(r.Path, query))
                    return;

                var ranges = SearchResultWithHighlightBinarySerializer.FlattenMask(FuzzyMatcher.ComputeHighlightMask(r.Name, query));
                writeLock.Wait();
                try
                {
                    SearchResultWithHighlightBinarySerializer.WriteFileResultAsync(pipe, r, ranges, token).GetAwaiter().GetResult();

                    // Inside the lock, because the buffer this flushes is the one the write above filled
                    // and BufferedStream is not safe to touch from two threads at once. The lock already
                    // serialises the writes for exactly that reason.
                    written++;
                    if (written <= FlushEveryResultUntil || written % FlushEveryResults == 0)
                        pipe.Flush();
                }
                finally
                {
                    writeLock.Release();
                }
            },
            token,
            null,
            bypassExclusions);
    }

    // A query token needs the FULL, already-ranked candidate set before a plugin-provided
    // IQueryTokenProvider can filter/reorder it (e.g. "::expr" fuzzy-matches by path segment, ".ext"
    // filters by extension) -- there's no meaningful way to stream this incrementally the way the plain
    // path above does, so this buffers everything, ranks it, dispatches the tokens, then sends the whole
    // already-final-order result in one go, same as SearchQueryDispatchController's own
    // RefreshAfterTokenDispatchAsync. PluginManager.QueryTokenProviders is only populated in a process
    // that's loaded plugins -- same reason AliasProviderRegistry needed this pipe in the first place --
    // so this dispatch has to run here, not on a bare CLI client.
    private static async Task RunTokenizedSearchAsync(string query, IReadOnlyList<string> tokens, string? directoryFilter, bool bypassExclusions, Stream pipe, CancellationToken token)
    {
        var raw = new List<SearchResult>();
        // SearchStreamingAsync's onResult callback fires concurrently from its local/network tasks, and
        // List<T>.Add is not safe under that -- same write-lock pattern RunStreamingSearchAsync uses.
        var writeLock = new SemaphoreSlim(1, 1);
        await SharedSearchService.SearchStreamingAsync(
            query,
            SearchViewModel.FullSearchFileLimit,
            SearchViewModel.FullSearchAppLimit,
            directoryFilter,
            r =>
            {
                writeLock.Wait();
                try { raw.Add(r); }
                finally { writeLock.Release(); }
            },
            token,
            null,
            bypassExclusions);

        SearchResultMapper.RemoveQueriedDirectoryItself(raw, query);
        token.ThrowIfCancellationRequested();
        raw.Sort(new SearchResultRankComparer(SearchHistoryStore.Snapshot()));

        var byPath = new Dictionary<string, SearchResult>(StringComparer.OrdinalIgnoreCase);
        var appResults = new List<AppSearchResult>(raw.Count);
        for (var i = 0; i < raw.Count; i++)
        {
            token.ThrowIfCancellationRequested();
            byPath[raw[i].Path] = raw[i];
            appResults.Add(SearchResultMapper.CreateUiResult(raw[i], query, i, isApplication: false, scope: null));
        }

        var dispatched = await QueryTokenDispatcher.ApplyAsync(appResults, tokens, token);

        // Highlight against each item's own (possibly token-extended) SearchQuery -- not the bare
        // `query` -- so a result kept alive by e.g. an "::expr" token highlights the same characters the
        // real GUI's TextHighlighter would, since QueryTokenDispatcher.ApplyAsync can append extra
        // highlight terms onto SearchQuery per result.
        var written = 0;
        foreach (var item in dispatched)
        {
            token.ThrowIfCancellationRequested();
            if (!byPath.TryGetValue(item.FullPath, out var original))
                continue;
            var ranges = SearchResultWithHighlightBinarySerializer.FlattenMask(FuzzyMatcher.ComputeHighlightMask(item.Name, item.SearchQuery));
            await SearchResultWithHighlightBinarySerializer.WriteFileResultAsync(pipe, original, ranges, token);

            // This path already has the whole ranked set in hand, so the flushes are purely so a large
            // one reaches the client as it goes rather than in a single burst at the End frame.
            written++;
            if (written <= FlushEveryResultUntil || written % FlushEveryResults == 0)
                await pipe.FlushAsync(token);
        }
    }

    private static char GetGlobalTokenPrefixChar()
    {
        var prefix = UserSettings.Load().GlobalTokenPrefix;
        return !string.IsNullOrEmpty(prefix) ? prefix[0] : '\\';
    }
}
