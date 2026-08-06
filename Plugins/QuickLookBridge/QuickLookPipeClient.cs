using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using Lertaro.PluginSdk;

namespace Lertaro.Plugins.QuickLookBridge;

// Talks directly to QuickLook's (github.com/QL-Win/QuickLook) named pipe -- the exact same one its own
// CLI second-instance forwarding uses -- instead of spawning a process per preview. Pipe name and message
// IDs below are copied verbatim from QuickLook/PipeServerManager.cs; they're QuickLook's private
// implementation detail, not a published API, so a future QuickLook release could change them without
// notice and silently break this.
internal static class QuickLookPipeClient
{
    private const int ConnectTimeoutMs = 1000;

    // QuickLook.App.PipeMessages.Toggle, sent WITH a non-empty options string -- QuickLook's own
    // TogglePreview(path, options) only takes its "hide if already showing this same path" branch when
    // options is empty; a non-empty options string always routes to InvokePreviewWithOption(path,
    // options) instead (see ViewWindowManager.cs), i.e. plain show/update, same as the Invoke message,
    // plus whatever the options ask for. Used (with "-top") to keep the docked window topmost so it
    // doesn't get lost behind Lertaro's own window.
    private const string ToggleMessage = "QuickLook.App.PipeMessages.Toggle";

    // Must have a "-"/"--"/"/" prefix -- QuickLook's own CommandLineParser (Helpers/CommandLineParser.cs)
    // only records a bare word into its Values dictionary if it immediately follows an already-recorded
    // "-key" token; a lone word with nothing preceding it (exactly what a bare "top" is here, since it's
    // the only token) is silently dropped and cli.Has("top") never sees it. Confirmed against QuickLook's
    // actual source, not just its own doc comments -- the first attempt at this used a bare "top" and
    // silently had no effect.
    private const string TopOption = "-top";

    // QuickLook.App.PipeMessages.Close -- NOT just a hide: ClosePreview() calls _viewerWindow.Close(),
    // and its Closed event handler immediately does _viewerWindow = new ViewerWindow() (confirmed against
    // QuickLook's own source, ViewWindowManager.cs). So every Close we send genuinely destroys and
    // recreates QuickLook's window -- any cached hwnd from before this point is guaranteed stale
    // afterward, not just possibly stale (see TryClosePreview).
    private const string CloseMessage = "QuickLook.App.PipeMessages.Close";

    // Anything QuickLook's own PipeMessages switch doesn't recognize falls into its `default: return
    // false` branch -- a real message ID with zero visible side effect, used purely to test reachability.
    private const string PingMessage = "Lertaro.Plugins.QuickLookBridge.Ping";

    private static readonly string PipeName =
        "QuickLook.App.Pipe." + (WindowsIdentity.GetCurrent().User?.Value ?? string.Empty);

    // IsAvailable() is called synchronously from CanPreview, on the UI thread, once per navigated-to
    // file -- during a burst of typing that's once per keystroke. A blocking pipe probe there (even a
    // successful one is a real cross-process round trip, not free) reads as UI stutter, and hammering the
    // pipe that rapidly also raises the odds of catching QuickLook's server between
    // Disconnect()/WaitForConnection() cycles (it handles one connection at a time) and reading that as a
    // spurious failure. So this never blocks the caller: it always returns the last known value instantly
    // and kicks off a background refresh, at most once per RefreshIntervalMs and never more than one at a
    // time, to keep that value from going stale for more than about a second.
    private const int RefreshIntervalMs = 1000;
    private static long _lastRefreshStartedTicks;
    private static int _refreshInFlight;
    private static volatile bool _cachedAvailable;

    // The one exception to "never blocks": _cachedAvailable defaults to false, so without this the very
    // first call in the process's lifetime would always report unavailable even if QuickLook is actually
    // running, since the background refresh hasn't had a chance to complete yet -- a real cold-start
    // failure, not a caching-staleness one, so a shorter TTL wouldn't have fixed it either. This blocks
    // synchronously exactly once (a bounded, one-time cost, not a per-navigation one) so that first real
    // answer is trustworthy; every call after it goes through the non-blocking path above.
    private static int _hasCheckedOnce;

    public static bool IsAvailable()
    {
        if (Interlocked.CompareExchange(ref _hasCheckedOnce, 1, 0) == 0)
        {
            SetCachedAvailable(TrySend(PingMessage, string.Empty, string.Empty));
            Interlocked.Exchange(ref _lastRefreshStartedTicks, DateTime.UtcNow.Ticks);
            return _cachedAvailable;
        }

        MaybeStartBackgroundRefresh();
        return _cachedAvailable;
    }

    // A true -> false transition here is a strong signal QuickLook's process itself just exited (or
    // crashed), covering the case TryClosePreview's own Reset() call can't: the process disappearing out
    // from under us entirely, e.g. between navigations rather than because we asked it to close.
    private static void SetCachedAvailable(bool available)
    {
        if (_cachedAvailable && !available)
            QuickLookWindowPositioner.Reset();
        _cachedAvailable = available;
    }

    private static void MaybeStartBackgroundRefresh()
    {
        var nowTicks = DateTime.UtcNow.Ticks;
        var lastTicks = Interlocked.Read(ref _lastRefreshStartedTicks);
        if (nowTicks - lastTicks < RefreshIntervalMs * TimeSpan.TicksPerMillisecond)
            return;

        if (Interlocked.CompareExchange(ref _refreshInFlight, 1, 0) != 0)
            return; // a refresh is already running

        Interlocked.Exchange(ref _lastRefreshStartedTicks, nowTicks);
        Task.Run(() =>
        {
            try { SetCachedAvailable(TrySend(PingMessage, string.Empty, string.Empty)); }
            finally { Interlocked.Exchange(ref _refreshInFlight, 0); }
        });
    }

    // Also called synchronously from the UI thread on every single navigation (CreatePreview/
    // TrySetTarget/EndPreviewSession). Originally queued strictly in order (one Task.ContinueWith chain),
    // to guarantee an earlier Invoke's send never completed after a later one's -- but a strict FIFO
    // queue is exactly wrong when requests arrive faster than they can be sent: rapidly paging through a
    // long result list (arrow-key-held or fast scroll, observed at 20-50ms per file -- far faster than one
    // real pipe round trip) queued up dozens of sends, and since each one really did get sent in order,
    // QuickLook just fell further and further behind, showing files the user had already scrolled past
    // long before catching up to the current one -- which reads as "invoke failure" even though every
    // individual send succeeded. What actually matters is only ever the MOST RECENT request: if a newer
    // one arrives before the previous one has even started sending, the old one is already irrelevant and
    // is dropped instead of queued, so the worker only ever sends whatever was truly latest once it's free.
    private enum PendingKind { None, Invoke, Close }

    private static readonly object QueueLock = new();
    private static PendingKind _pendingKind;
    private static string _pendingPath = string.Empty;
    private static bool _workerRunning;

    public static void TryInvokePreview(string path) => Enqueue(PendingKind.Invoke, path);

    public static void TryClosePreview()
    {
        // Reset BEFORE sending, not after: QuickLook destroys and recreates its window as a direct,
        // synchronous(-ish) consequence of receiving this message (see CloseMessage's own comment), so the
        // cached hwnd is guaranteed stale from the moment this is sent, not just probably stale by the
        // time the send completes. Safe to do even if this particular Close ends up superseded/dropped
        // below before ever actually being sent -- resetting our own local cache early is harmless either
        // way, at worst costing one extra re-poll later.
        QuickLookWindowPositioner.Reset();
        Enqueue(PendingKind.Close, string.Empty);
    }

    private static void Enqueue(PendingKind kind, string path)
    {
        lock (QueueLock)
        {
            _pendingKind = kind;
            _pendingPath = path;
            if (_workerRunning) return;
            _workerRunning = true;
        }
        Task.Run(RunPendingWorker);
    }

    private static void RunPendingWorker()
    {
        while (true)
        {
            PendingKind kind;
            string path;
            lock (QueueLock)
            {
                if (_pendingKind == PendingKind.None)
                {
                    _workerRunning = false;
                    return;
                }
                kind = _pendingKind;
                path = _pendingPath;
                _pendingKind = PendingKind.None;
            }

            if (kind == PendingKind.Invoke)
                TrySend(ToggleMessage, path, TopOption);
            else
                TrySend(CloseMessage, string.Empty, string.Empty);
        }
    }

    private static bool TrySend(string pipeMessage, string path, string options)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(ConnectTimeoutMs);

            // QuickLook's server does an unconditional reader.ReadLine() with no null guard, so a client
            // that connects without ever writing a line crashes its read loop (NullReferenceException on
            // the null result) and takes down the pipe for the rest of that QuickLook session -- always
            // write a real line, even for the no-op ping.
            using var writer = new StreamWriter(client);
            writer.WriteLine($"{pipeMessage}|{path}|{options}");
            writer.Flush();
            Logger.Log($"[QuickLookBridge] pipe send ok: {pipeMessage} '{path}' options='{options}' (pipe='{PipeName}')", LogLevel.Debug);
            return true;
        }
        catch (Exception ex)
        {
            Logger.Log($"[QuickLookBridge] pipe send FAILED: {pipeMessage} '{path}' options='{options}' (pipe='{PipeName}') -> {ex.GetType().Name}: {ex.Message}", LogLevel.Warn);
            return false;
        }
    }
}
