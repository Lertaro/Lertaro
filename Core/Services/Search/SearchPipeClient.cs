using System.IO.Pipes;
using Lertaro.Core.Indexer.Usn;

using Lertaro.Core.Wire;
namespace Lertaro.Core.Services.Search;

/// <summary>
/// Thin named-pipe RPC client to the elevated search service: each method wraps one request/response
/// round trip over the "LertaroPipe" named pipe. Kept separate from <see cref="SearchService"/>,
/// which owns multi-source search orchestration (merging pipe results with in-process network-drive
/// and live-directory search) -- that's a different responsibility from "talk to the service process."
/// </summary>
internal sealed class SearchPipeClient
{
    private static async Task<NamedPipeClientStream> GetPipeAsync(CancellationToken token)
    {
        var pipe = new NamedPipeClientStream(".", "LertaroPipe", PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(2000, token).ConfigureAwait(false);
        // The service listening is the readiness signal: until this first succeeds, connect
        // failures elsewhere log as cold-start noise instead of real faults.
        ServicePipeReadinessGate.Instance.MarkConnected();
        return pipe;
    }

    public async Task<UsnIndexer.IndexerStatus> GetStatusAsync(CancellationToken token = default)
    {
        var resp = await SendPipeCommandAsync(new SearchRequestMessage { Id = SearchRequestId.Status }, token).ConfigureAwait(false);
        if (resp.Kind == PipeResponseKind.Status && resp.Status != null) return resp.Status;
        if (resp.Kind == PipeResponseKind.Error) Logger.Log($"[SearchService] STATUS failed: {resp.Message}", LogLevel.Error);
        return new UsnIndexer.IndexerStatus { State = "error" };
    }

    public async Task<bool> PingAsync(CancellationToken token = default)
    {
        var resp = await SendPipeCommandAsync(new SearchRequestMessage { Id = SearchRequestId.Ping }, token).ConfigureAwait(false);
        return resp.Kind == PipeResponseKind.Ok;
    }

    // Asks the already-running --service instance to spawn the hook process directly into this caller's
    // own session (see HookProcessBroker) -- the App itself never launches the hook process anymore, so
    // it never has a "runas" UAC prompt of its own to show. requestElevation is only honored server-side
    // when that session's user is genuinely an administrator; otherwise it just launches non-elevated.
    public async Task<(bool Ok, int Pid, string? Error)> RequestHookLaunchAsync(bool requestElevation, CancellationToken token = default)
    {
        var resp = await SendPipeCommandAsync(new SearchRequestMessage { Id = SearchRequestId.LaunchHook, RequestElevation = requestElevation }, token).ConfigureAwait(false);
        return resp.Kind == PipeResponseKind.HookLaunched ? (true, resp.Pid, null) : (false, 0, resp.Message);
    }

    // Fire-and-forget, called whenever a search window closes/hides (mirrors ShellIconHelper.ClearCache()'s
    // existing trigger points) -- gives back the local drives' per-row full-path memo, which otherwise
    // only self-clears once it crosses its own high backstop threshold (see PathQueryExtensions).
    public async Task ClearPathCachesAsync(CancellationToken token = default)
    {
        var resp = await SendPipeCommandAsync(new SearchRequestMessage { Id = SearchRequestId.ClearPathCaches }, token).ConfigureAwait(false);
        if (resp.Kind == PipeResponseKind.Error) Logger.Log($"[SearchService] CLEAR_PATH_CACHES failed: {resp.Message}", LogLevel.Error);
    }

    public async Task<PipeResponse> SendPipeCommandAsync(SearchRequestMessage msg, CancellationToken token)
    {
        try
        {
            var verboseLog = msg.Id != SearchRequestId.Search && msg.Id != SearchRequestId.SearchDir;
            if (verboseLog)
                Logger.Log($"[PipeClient] Connecting to pipe for command: {msg.Id}...", LogLevel.Debug);
            using var pipe = new NamedPipeClientStream(".", "LertaroPipe", PipeDirection.InOut, PipeOptions.Asynchronous);

            await pipe.ConnectAsync(2000, token).ConfigureAwait(false);
            if (verboseLog)
                Logger.Log("[PipeClient] Connected. Writing command...", LogLevel.Debug);
            await SearchRequestBinarySerializer.WriteSearchRequestAsync(pipe, msg, token).ConfigureAwait(false);
            if (verboseLog)
                Logger.Log("[PipeClient] Command written. Reading response...", LogLevel.Debug);
            var resp = await PipeResponseBinarySerializer.ReadAsync(pipe, token).ConfigureAwait(false);
            if (verboseLog)
                Logger.Log($"[PipeClient] Response received: {resp.Kind}.", LogLevel.Debug);
            return resp;
        }
        catch (Exception ex)
        {
            // Warn, not Error: this fires routinely on a cold start (App connects before the Service has
            // finished coming up) and is expected to self-heal via the caller's own retry -- callers that
            // need to surface a persistent failure to the user already re-log at Error with more context.
            // Inside the cold-start window it is Debug outright: a batch of these on every boot is
            // noise, the callers' fallbacks answer meanwhile and nothing is actually broken.
            var level = ServicePipeReadinessGate.Instance.IsColdStart(Environment.TickCount64)
                ? LogLevel.Debug
                : LogLevel.Warn;
            Logger.Log($"[PipeClient] SendPipeCommand failed for {msg.Id}: {ex.Message}", level);
            return new PipeResponse { Kind = PipeResponseKind.Error, Message = ex.Message };
        }
    }

    // Response buffer for the streaming read below. Large enough that one read covers several hundred
    // results, small enough to stay off the large object heap.
    private const int ResponseReadBufferSize = 64 * 1024;

    public static async Task SendSearchPipeCommandAsync(SearchRequestMessage msg, Action<SearchResult> onResult, CancellationToken token, Action? onNotIndexed = null)
    {
        using var pipe = await GetPipeAsync(token).ConfigureAwait(false);
        await SearchRequestBinarySerializer.WriteSearchRequestAsync(pipe, msg, token).ConfigureAwait(false);

        // Read through a buffer, because SearchResponseBinarySerializer.ReadAsync reads a result's magic,
        // frame type, payload length and payload as four separate calls. Straight onto the pipe -- whose
        // own buffer is 4KB -- that is four syscalls per result and it dominated everything: measured
        // over 200k results with the service's real write path, 6.0 seconds against 0.41 with this
        // buffer, 30us a result against 2.1. For a search returning every match on a drive that was
        // twenty seconds of the wall clock.
        //
        // Reading ahead past the End frame is safe here specifically because GetPipeAsync hands back a
        // brand new connection per request and it is disposed on the way out of this method -- nothing
        // else ever reads from it, so there are no following bytes to steal. Buffering only affects how
        // the bytes are fetched, never what they are, so a new client still talks to an old service.
        // The request above is deliberately written to the raw pipe rather than through this, so the two
        // directions cannot interleave in one buffer.
        await using var buffered = new BufferedStream(pipe, ResponseReadBufferSize);

        await SearchResponseBinarySerializer.ReadAsync(buffered, result =>
        {
            token.ThrowIfCancellationRequested();
            onResult(result);
        }, token, onNotIndexed).ConfigureAwait(false);
    }
}
