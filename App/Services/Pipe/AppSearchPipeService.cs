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

    // Strip trailing query tokens before searching, as SearchQueryDispatchController does. After the
    // search, IQueryTokenProvider plugins filter or reorder the ranked results.
    //
    // File-filter scope keywords are not resolved on this wire path: "lff tf report" searches the
    // literal words, while the GUI can apply tf's configured folders. Supporting scopes here requires
    // carrying the directive through search and serialization.
    //
    // Buffer pipe writes to reduce per-result syscalls. Flush each of the first ten results, every
    // fiftieth result thereafter, and the final remainder, matching SearchStreamPump's latency policy.
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

    // Stream untokenized results as they arrive; the client ranks each growing snapshot with
    // SearchResultRankComparer. Tokenized searches collect and rank server-side with SearchResultRanking
    // and RankedSearchResultComparer before applying tokens.
    //
    // A bounded channel serializes writes from concurrent local/network callbacks. The callback computes
    // highlights here, where alias plugins are loaded, then enqueues the result. A stalled client retains
    // at most ResultQueueDepth queued rows and one pending write. Cancellation releases both, allowing
    // HandleClientAsync to clear IdleWorkingSetTrimmer's active-search counter.
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
    // path above does, so this buffers everything, ranks it with the full window's ordering (see
    // RunTokenizedSearchAsync: SearchResultRanking.Decorate + RankedSearchResultComparer, i.e. sections,
    // decayed history, favorites and pins), dispatches the tokens, then sends the whole
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
        // The full window's ordering, once over the complete set: sections and score written onto each row,
        // then compared by the shared comparer. History is read as a single decayed snapshot for this query
        // (the old ladder used the undecayed open count), and favorites/pins come from the same context, so
        // a token search answers with the order the GUI's own full window would show.
        var rankRules = ExclusionRuleSet.From(UserSettings.Load());
        var rankContext = new RankContext(
            rankRules.GetPriorityVerdict,
            SearchHistoryStore.ScoreSnapshot(DateTimeOffset.UtcNow),
            FavoritePaths());

        foreach (var result in raw)
            SearchResultRanking.Decorate(result, rankContext);

        raw.Sort(RankedSearchResultComparer.Instance);

        var byPath = new Dictionary<string, SearchResult>(StringComparer.OrdinalIgnoreCase);
        var appResults = new List<AppSearchResult>(raw.Count);
        for (var i = 0; i < raw.Count; i++)
        {
            token.ThrowIfCancellationRequested();
            byPath[raw[i].Path] = raw[i];
            appResults.Add(SearchResultMapper.CreateUiResult(raw[i], query, i, isApplication: false, scope: null));
        }

        var dispatched = await QueryTokenDispatcher.ApplyAsync(appResults, tokens, query, token);

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

    private static IReadOnlySet<string> FavoritePaths()
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var favorite in UserSettings.Load().Favorites)
        {
            try
            {
                paths.Add(SearchResultHelper.NormalizePath(
                    Helpers.FavoritePathResolver.Resolve(favorite.Path)));
            }
            catch
            {
            }
        }
        return paths;
    }

    private static char GetGlobalTokenPrefixChar()
    {
        var prefix = UserSettings.Load().GlobalTokenPrefix;
        return !string.IsNullOrEmpty(prefix) ? prefix[0] : ':';
    }
}
