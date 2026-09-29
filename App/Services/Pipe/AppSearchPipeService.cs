using System.IO;
using System.IO.Pipes;
using System.Threading.Channels;
using Lertaro.Core;
using Lertaro.App.ViewModels.Search;
using Lertaro.App.ViewModels.Search.Dispatch;

using Lertaro.Core.Services.Search;
using Lertaro.Core.Services.Pipe;
using Lertaro.Core.Wire;
using Lertaro.Core.SearchIndex;
using Lertaro.Core.SearchIndex.Query;
using Lertaro.App.ViewModels.Search.Mapping;
namespace Lertaro.App.Services.Pipe;

using SearchWindowType = PluginSdk.Abstractions.SearchWindowType;

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
    // Two independent layers, matching how AppPipeService's own activation pipe scopes itself, plus one
    // more: the per-SID and per-session suffix means a different Windows account's App instance never contends for
    // the exact same pipe name in the first place (Windows named pipes live in the machine-wide \\.\pipe\
    // namespace, not session-isolated by default), and the ACL below backs that with actual enforcement --
    // the OS itself rejects a connection attempt from any SID but the current user's, so even a guessed/
    // predicted name (Windows usernames aren't secret) can't cross accounts. This matters specifically for
    // this pipe (unlike the plain activation one) because a search request can return another user's own
    // file paths/network-drive contents.
    private static readonly string PipeName = AppPipeNames.SearchPipeName;
    private static bool _keepRunning = true;
    private static readonly SearchService SharedSearchService = new();

    public static void StopServer() => _keepRunning = false;

    public static Task StartPipeServerAsync() => Task.Run(ListenLoopAsync);

    private static async Task ListenLoopAsync()
    {
        // PipeSecurityFactory.CreateCurrentUserOnly's ACL (SID-based), not the simpler
        // PipeOptions.CurrentUserOnly flag: this pipe needs to be reachable from an ELEVATED client too
        // (`lff` run from an admin terminal), and PipeOptions.CurrentUserOnly's own client-side check
        // compares token OWNER, not the actual user SID -- for a member of Administrators that's
        // BUILTIN\Administrators on both the standard and elevated token, not this (non-elevated) App's
        // own user SID, so an elevated client fails that check even though it's the very same logged-in
        // user. See CreateCurrentUserOnly's own comment for the full explanation.
        var pipeSecurity = PipeSecurityFactory.CreateCurrentUserOnly();
        if (pipeSecurity == null)
        {
            // No PipeOptions.CurrentUserOnly fallback here (unlike an earlier version of this method) --
            // that flag is precisely the buggy mechanism the ACL above replaced (see the comment on
            // CreateCurrentUserOnly), so silently falling back to it would quietly reintroduce the exact
            // "elevated client rejected" bug this exists to avoid, in whatever rare case
            // WindowsIdentity.GetCurrent().User itself fails to resolve. Unlike HookIpcServer's own
            // fallback (a plain, unrestricted pipe), this one also isn't an acceptable substitute here:
            // this pipe's results can carry another user's own file paths/network-drive contents (see the
            // PipeName comment above), so a broadened ACL is a real exposure, not just a shrug-worthy
            // degradation. Refusing to start is the honest failure mode.
            Logger.Log("[AppSearchPipeService] Could not resolve the current user's SID -- refusing to start (would otherwise need to either reintroduce a known bug or broaden this pipe's ACL, neither acceptable).", LogLevel.Error);
            return;
        }

        while (_keepRunning)
        {
            NamedPipeServerStream? pipe = null;
            try
            {
                pipe = NamedPipeServerStreamAcl.Create(
                    PipeName,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous,
                    4096, 4096,
                    pipeSecurity);

                await pipe.WaitForConnectionAsync().ConfigureAwait(false);
                _ = Task.Run(() => HandleClientAsync(pipe));
            }
            catch (Exception ex)
            {
                pipe?.Dispose();
                Logger.Log($"[AppSearchPipeService] Server connection failed: {ex.Message}", LogLevel.Error);
                await Task.Delay(1000).ConfigureAwait(false);
            }
        }
    }

    private static async Task HandleClientAsync(NamedPipeServerStream pipe)
    {
        using (pipe)
        {
            try
            {
                while (pipe.IsConnected)
                {
                    var request = await SearchRequestBinarySerializer.ReadSearchRequestAsync(pipe);
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

    // Mirrors SearchQueryDispatchController.OnAdvancedQueryChanged's query preparation -- including the
    // part an earlier version of this method skipped: a trailing " :a,b,c" suffix
    // (SearchQuerySortParser.Strip) isn't part of the fuzzy search text at all -- it's dispatched, AFTER
    // the file search completes, to whichever IQueryTokenProvider plugin (the built-in "::expr"/".ext"/etc.)
    // claims each token, which can filter or reorder the already-ranked results. Passing the raw
    // (unstripped) query straight into SearchStreamingAsync -- what this used to do -- searched for the
    // literal ":xxx" substring instead of treating it as an operator, which is why that syntax silently
    // did nothing here.
    // One deliberate gap, so that "mirrors" is not read stronger than it is: there is no
    // FileFilterScopeResolver.Resolve here, so a configured file-filter scope keyword searches its
    // filter's folders in the GUI but is matched as literal text here ("lff tf report" finds nothing the
    // window's "tf report" finds). Closing it is not a call away -- the directive rides all the way
    // through SearchExecutionEngine into SearchResultMapper's row construction, which this wire path
    // replaces with its own serialization.
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
            var globalPrefixChar = GetGlobalTokenPrefixChar();
            var strippedTrailing = SearchQuerySortParser.Strip(query, out var tokens, globalPrefixChar);
            var cleanQuery = SearchQuerySortParser.StripExclusionBypass(strippedTrailing, out var bypassExclusions);
            // The same trigger-word strip the full window applies, so a CLI query and the identical text
            // typed in the window search the same thing rather than matching "cs" against file names here.
            // The full window's own inventory (SearchWindowType.Main) is what a CLI client is asking for.
            // ponytail: this runs on the pipe's thread while the GUI runs it on the UI thread; a provider
            // reading its own settings is a dictionary lookup, but a plugin with non-thread-safe state in
            // QueryTriggerKeywords could be read concurrently. Upgrade path: marshal to the dispatcher.
            cleanQuery = PluginTriggerQuery.Strip(cleanQuery, SearchWindowType.Main);

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
    //
    // Writes go through a bounded channel to one consumer task, which is what SearchStreamPump already
    // does on the elevated service's pipe. The callback below fires from whichever of
    // SearchStreamingAsync's local/network tasks produced a match, so it used to take a SemaphoreSlim and
    // then block on the pipe write with GetAwaiter().GetResult() -- sync-over-async on a thread-pool
    // thread, once per result, with nothing able to time it out. A client that stayed connected but
    // stopped reading therefore parked a pool thread indefinitely, and because the only thing that ends
    // that wait is the write completing, the finally in HandleClientAsync never ran and left
    // IdleWorkingSetTrimmer's background-search counter permanently elevated (see
    // IdleWorkingSetTrimGate.ShouldTrim).
    //
    // Now the callback only computes the highlight mask -- CPU work that has to happen here rather than on
    // the client, because FuzzyMatcher.ComputeHighlightMask needs the alias plugins THIS process loaded --
    // and hands the row to the channel. A slow client costs at most ResultQueueDepth rows of queue and one
    // awaited write, both of which cancellation can reach.
    private const int ResultQueueDepth = 1024;

    // A pipe write is the one step in this path that can block forever: a client that is connected but not
    // reading never trips the disconnect watchdog, which polls for ERROR_BROKEN_PIPE and finds a live pipe.
    // Without a deadline the stalled request stays in flight for the life of the process, so the queue
    // bound above would only move the permanent wait from a pool thread to the search's own threads. Thirty
    // seconds is far past any real client's scheduling gap.
    private static readonly TimeSpan PipeWriteTimeout = TimeSpan.FromSeconds(30);

    private static async Task RunStreamingSearchAsync(string query, string? directoryFilter, bool bypassExclusions, Stream pipe, CancellationToken token)
    {
        // Cancels the whole request once either the client goes away or a write is judged stalled -- the
        // producer below waits on it too, so abandoning the response also stops the search that is still
        // feeding it rather than leaving it producing rows with nowhere to go.
        using var abandoned = CancellationTokenSource.CreateLinkedTokenSource(token);
        var pending = Channel.CreateBounded<(SearchResult Row, int[] Ranges)>(new BoundedChannelOptions(ResultQueueDepth)
        {
            SingleReader = true,
            // The callback really does fire from the local and the network task at once, so the
            // SingleWriter fast path is not available here (same reasoning as SearchStreamPump's).
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait,
        });

        var writing = WriteQueuedResultsAsync(pending.Reader, pipe, token, abandoned, PipeWriteTimeout);
        try
        {
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
                    // Blocking here is the point: a full queue is backpressure into the scan, and unlike the
                    // pipe write it is always released -- by the consumer, or by `abandoned`.
                    pending.Writer.WriteAsync((r, ranges), abandoned.Token).AsTask().GetAwaiter().GetResult();
                },
                abandoned.Token,
                null,
                bypassExclusions);
        }
        finally
        {
            pending.Writer.TryComplete();
        }

        await writing.ConfigureAwait(false);
    }

    // The single writer the wire needs: rows leave in the order the channel hands them out, so two
    // results' bytes can never interleave and BufferedStream is only ever touched from here. Internal and
    // carrying its own deadline so a stall can be tested without waiting out the production timeout.
    internal static async Task WriteQueuedResultsAsync(ChannelReader<(SearchResult Row, int[] Ranges)> reader, Stream pipe,
        CancellationToken token, CancellationTokenSource abandoned, TimeSpan writeTimeout)
    {
        using var writeDeadline = CancellationTokenSource.CreateLinkedTokenSource(abandoned.Token);
        var written = 0;
        await foreach (var (row, ranges) in reader.ReadAllAsync(abandoned.Token).ConfigureAwait(false))
        {
            // Re-armed per row: this measures whether the client is keeping up, not how long the search
            // takes -- a whole-drive query is expected to run for minutes.
            writeDeadline.CancelAfter(writeTimeout);
            try
            {
                await SearchResultWithHighlightBinarySerializer.WriteFileResultAsync(pipe, row, ranges, writeDeadline.Token).ConfigureAwait(false);

                written++;
                if (written <= FlushEveryResultUntil || written % FlushEveryResults == 0)
                    await pipe.FlushAsync(writeDeadline.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                // The deadline expired rather than the client disconnecting. Nothing else can unstick this,
                // and the search has to go with it: it is still producing rows into a queue nobody drains.
                abandoned.Cancel();
                throw new IOException($"Aborting a piped search: no write completed within {writeTimeout.TotalSeconds:0.##}s, so the client has stopped reading.");
            }
        }
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

        var dispatched = await QueryTokenDispatcher.ApplyAsync(appResults, tokens);

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
        return !string.IsNullOrEmpty(prefix) ? prefix[0] : ':';
    }
}
