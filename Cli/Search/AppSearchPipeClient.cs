using System.IO.Pipes;
using Lertaro.Core;

using Lertaro.Core.Wire;
namespace Lertaro.Cli.Search;

// The wire-level half of talking to AppSearchPipeService (App\Services\AppSearchPipeService.cs): connects
// to the App's own search pipe and reads back streamed (SearchResult, highlight-ranges) pairs.
// SearchSession owns the query-debouncing/ranking/session-state side of things; this class only knows how
// to ask the pipe a question and hand back what it says.
public static class AppSearchPipeClient
{
    public const int ConnectTimeoutMs = 1500;

    // Response buffer for the streaming read below. Large enough that one read covers several hundred
    // results, small enough to stay off the large object heap.
    private const int ResponseReadBufferSize = 64 * 1024;

    // Must match AppSearchPipeService's own naming exactly (per-user suffix + ACL) -- see that file's
    // comment for why: a search response can carry another account's own file paths/network-drive
    // contents, so this pipe deliberately isn't shared machine-wide the way the plain activation pipe is.
    public static string PipeNameFor(string userName) => $"Lertaro_App_Search_Pipe_{userName}";

    // Verified reachable BEFORE the interactive UI ever opens a console handle: a script or pipeline that
    // invokes `lff` non-interactively (the whole point of the ffmpeg/`for /f` usage this was built for)
    // has no human sitting there to notice an inline "[error] ... is the App running?" status line and
    // press Escape -- it would just hang forever waiting for keystrokes nobody's going to send. Throws on
    // failure; the caller reports it and exits before any console handle is opened, so stdout stays
    // completely empty (nothing a caller could mistake for a real, if odd, result) and the actual reason
    // goes to stderr, matching how any other well-behaved CLI reports "couldn't do the thing" instead of
    // getting stuck. Only covers the App being unreachable at startup, not a mid-session disconnect once
    // the interactive UI is already up and a human is presumably already looking at it -- SearchAsync's
    // own per-search connect (same ConnectTimeoutMs) surfaces that later case as an inline status-line
    // message instead.
    public static async Task ProbeAsync(string pipeName)
    {
        // No PipeOptions.CurrentUserOnly: that flag makes NamedPipeClientStream do its OWN client-side
        // check that the pipe's owner SID matches WindowsIdentity.GetCurrent().Owner, which is
        // BUILTIN\Administrators (not this user's own SID) for an elevated process belonging to a member
        // of Administrators -- so `lff` run from an admin terminal was throwing UnauthorizedAccessException
        // ("was not owned by the current user") against a perfectly reachable, correctly-ACL'd pipe. The
        // actual security boundary is the server's ACL (see PipeSecurityFactory.CreateCurrentUserOnly's
        // own comment, App\Services\AppSearchPipeService.cs), which checks the real user SID and doesn't
        // have this problem -- a client from a genuinely different Windows account still gets rejected by
        // the OS itself, just via the ACL rather than this redundant extra check.
        using var probePipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        using var probeCts = new CancellationTokenSource(ConnectTimeoutMs);
        await probePipe.ConnectAsync(ConnectTimeoutMs, probeCts.Token);
    }

    // One fresh connection per search (mirrors SearchService.SendSearchPipeCommandAsync's own pattern for
    // LertaroPipe) -- avoids interleaving a newer query's request bytes with an older query's still-
    // streaming response on a shared connection.
    //
    // AppSearchPipeService forwards each result the instant it finds one, unsorted -- so this reads the
    // stream incrementally instead of waiting for ReadAsync to fully return before showing anything (that
    // wait was the actual cause of feeling slower than the GUI). The intermediate-render gating below is a
    // direct port of SearchExecutionEngine.PerformStreamingSearchAsync's own logic (App\ViewModels\Search\
    // SearchExecutionEngine.cs) -- not a recurring poll: exactly ONE early callback fires, gated on both
    // "at least 9 results have streamed in" AND "at least 40ms have passed since this search started"
    // (whichever condition is met later), via Interlocked.CompareExchange so only one intermediate
    // callback ever gets scheduled; then one final callback once the stream ends. The 40ms wait, when
    // needed, is a single Task.Delay continuation tied to that specific already-streamed data, not an
    // independent timer checking state on a schedule.
    public static async Task<List<(SearchResult Result, int[] Highlights)>> SearchAsync(string pipeName, string query, Action<List<(SearchResult, int[])>> onProgress, CancellationToken token)
    {
        // No PipeOptions.CurrentUserOnly here either -- see ProbeAsync's own comment on why (the ACL on
        // AppSearchPipeService's side is the real enforcement; this flag would just make an elevated
        // `lff` fail its own redundant client-side owner check against a non-elevated App).
        using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(ConnectTimeoutMs, token);

        var msg = new SearchRequestMessage { Id = SearchRequestId.Search, Query = query };
        await SearchRequestBinarySerializer.WriteSearchRequestAsync(pipe, msg, token);

        // Read through a buffer: ReadAsync takes a result's magic, frame type, payload length and
        // payload as four separate reads, which straight onto a pipe is four syscalls per result. The
        // same change on the GUI's own pipe measured 30us a result down to 2.1 over 200k results. Safe
        // to read ahead past the End frame because this connection is created just above and disposed on
        // the way out, so nothing else ever reads from it; the request is written to the raw pipe so the
        // two directions cannot interleave in one buffer.
        await using var buffered = new BufferedStream(pipe, ResponseReadBufferSize);

        var fresh = new List<(SearchResult, int[])>();
        var responseLock = new object();
        var streamedCount = 0;
        var renderState = 0; // 0 = not yet rendered, 1 = intermediate render scheduled/done, 2 = final
        var startTime = Environment.TickCount;

        void RenderSnapshot()
        {
            List<(SearchResult, int[])> snapshot;
            lock (responseLock) snapshot = new List<(SearchResult, int[])>(fresh);
            onProgress(snapshot);
        }

        await SearchResultWithHighlightBinarySerializer.ReadAsync(buffered, (r, highlights) =>
        {
            token.ThrowIfCancellationRequested();
            lock (responseLock)
            {
                fresh.Add((r, highlights));
                streamedCount++;
            }

            if (Volatile.Read(ref renderState) == 0 && Volatile.Read(ref streamedCount) < 9)
                return;
            if (Interlocked.CompareExchange(ref renderState, 1, 0) != 0)
                return;

            var elapsed = Environment.TickCount - startTime;
            if (elapsed < 40)
            {
                _ = Task.Delay(40 - elapsed, token).ContinueWith(t =>
                {
                    if (t.IsCanceled) return;
                    RenderSnapshot();
                }, token);
            }
            else
            {
                RenderSnapshot();
            }
        }, token);

        return fresh;
    }
}
