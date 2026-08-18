using System.IO;
using System.IO.Pipes;
using Lertaro.Core;
using Lertaro.App.ViewModels.Search;

using Lertaro.Core.Services.Search;

using Lertaro.Core.Services.Pipe;
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

                    if (request.Id != SearchRequestId.Search)
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
                        await RunFullWindowSearchAsync(request.Query ?? string.Empty, pipe, queryCts.Token);
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
    // earlier version of this method skipped: a trailing " :a,b,c" suffix (SearchQuerySortParser.Strip)
    // isn't part of the fuzzy search text at all -- it's dispatched, AFTER the file search completes, to
    // whichever IQueryTokenProvider plugin (the built-in "::expr"/".ext"/etc.) claims each token, which
    // can filter or reorder the already-ranked results. Passing the raw (unstripped) query straight into
    // SearchStreamingAsync -- what this used to do -- searched for the literal ":xxx" substring instead
    // of treating it as an operator, which is why that syntax silently did nothing here.
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

    private static async Task RunFullWindowSearchAsync(string query, Stream pipe, CancellationToken token)
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

            if (tokens.Count > 0)
                await RunTokenizedSearchAsync(cleanQuery, tokens, bypassExclusions, buffered, token);
            else
                await RunStreamingSearchAsync(cleanQuery, bypassExclusions, buffered, token);
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
    private static async Task RunStreamingSearchAsync(string query, bool bypassExclusions, Stream pipe, CancellationToken token)
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
            null,
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
    private static async Task RunTokenizedSearchAsync(string query, IReadOnlyList<string> tokens, bool bypassExclusions, Stream pipe, CancellationToken token)
    {
        var raw = new List<SearchResult>();
        await SharedSearchService.SearchStreamingAsync(
            query,
            SearchViewModel.FullSearchFileLimit,
            SearchViewModel.FullSearchAppLimit,
            null,
            r => raw.Add(r),
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
