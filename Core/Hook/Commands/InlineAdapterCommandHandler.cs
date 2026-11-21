using System.Text;
using Lertaro.PluginSdk.Registries;

using Lertaro.Core.Wire;
using Lertaro.PluginSdk.Abstractions.Plugins.WindowAdapters;
using Lertaro.Core.Hook.Ipc;
namespace Lertaro.Core.Hook.Commands;

// Runs inline-adapter writes in the Hook process so an elevated Hook can navigate elevated file managers
// across UIPI boundaries.
//
// Each call uses a separate STA thread for COM/UI Automation. A blocking adapter call, such as Total
// Commander's unbounded SendMessage, can leave that thread blocked but cannot stop ExplorerTracker's
// event thread.
internal static class InlineAdapterCommandHandler
{
    // Selection mirroring is coalesced rather than dropped. Each InlineSelectionChanged updates the
    // LATEST path/version and (if not already running) starts one STA worker that syncs the newest value
    // at most once per SelectionDebounceMs, then exits once nothing newer arrives.
    //
    // This replaces a per-message "sleep, then bail if superseded" debounce. That version discarded every
    // intermediate change, and InlineSelectionChanged fires on far more than arrow keys -- the App
    // re-selects each result set's first item on every keystroke, so a burst of typing superseded every
    // call and NOTHING was mirrored until the user paused. It also swallowed genuine arrow-key presses
    // that happened to land inside the burst. Coalescing keeps the original goal (never race one COM call
    // per keystroke) while guaranteeing the host follows the search as it refines, not only after it
    // settles.
    private static readonly object _selectionSyncGate = new();
    private static long _selectionVersion;
    private static long _selectionProcessed;
    private static string? _latestSelectionPath;
    private static IntPtr _latestSelectionHwnd;
    private static int _selectionWorkerRunning;
    private const int SelectionDebounceMs = 120;

    public static void Handle(HookProcess process, IpcMessage msg)
    {
        var hwnd = (IntPtr)msg.Hwnd;
        if (hwnd == IntPtr.Zero) return;

        switch (msg.Id)
        {
            case IpcMessageId.ExecuteInlineItem:
            case IpcMessageId.LocateInlineItem:
                var path = msg.StringVal1 ?? string.Empty;
                var searchInput = msg.StringVal2 ?? string.Empty;
                var requestId = msg.IntVal;
                RunOnSta(() =>
                {
                    // Adapter code is third-party-plugin-authored native/COM interop -- an uncaught
                    // exception here would take down whatever thread it ran on, so this must never
                    // propagate. On failure, still send a response so the App's blocking ExecuteItem call
                    // fails fast instead of timing out.
                    var result = false;
                    try
                    {
                        var adapter = ResolveAdapter(process, hwnd);
                        result = adapter is { NavigateInUserApp: false } && (msg.Id == IpcMessageId.LocateInlineItem
                            ? adapter.LocateItem(hwnd, path)
                            : adapter.ExecuteItem(hwnd, path, searchInput));
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"[InlineAdapterCommandHandler] ExecuteItem threw: {ex.Message}", LogLevel.Error);
                    }
                    process.IpcServer.SendMessage(new IpcMessage { Id = IpcMessageId.ExecuteInlineItemResponse, IntVal = requestId, BoolVal = result });
                });
                break;

            case IpcMessageId.InlineSelectionChanged:
                // Record the newest request first (IPC messages are handled one at a time, so the version
                // increment itself is race-free), then make sure a worker is draining it.
                lock (_selectionSyncGate)
                {
                    _latestSelectionPath = msg.StringVal1 ?? string.Empty;
                    _latestSelectionHwnd = hwnd;
                    Interlocked.Increment(ref _selectionVersion);
                }
                StartSelectionWorker(process);
                break;

            case IpcMessageId.InlineSearchFinished:
                var executed = msg.BoolVal;
                RunOnSta(() =>
                {
                    try { ResolveAdapter(process, hwnd)?.OnSearchFinished(hwnd, executed); }
                    catch (Exception ex) { Logger.Log($"[InlineAdapterCommandHandler] OnSearchFinished threw: {ex.Message}", LogLevel.Error); }
                });
                break;
        }
    }

    // Ensures exactly one worker is draining selection changes at a time; a call that finds one already
    // running just leaves its value for that worker to pick up.
    private static void StartSelectionWorker(HookProcess process)
    {
        if (Interlocked.CompareExchange(ref _selectionWorkerRunning, 1, 0) != 0)
            return;
        RunOnSta(() => DrainSelectionChanges(process));
    }

    // Syncs the newest (hwnd, path) once per SelectionDebounceMs and exits when nothing newer arrives.
    // Every iteration re-reads the latest values, so a burst collapses to a steady follow rather than one
    // COM call per keystroke -- and, unlike the old supersede-and-drop debounce, it always converges on
    // wherever the user actually is instead of discarding the whole burst until typing stops.
    private static void DrainSelectionChanges(HookProcess process)
    {
        try
        {
            while (true)
            {
                var version = Interlocked.Read(ref _selectionVersion);
                Thread.Sleep(SelectionDebounceMs);

                string? path;
                IntPtr hwnd;
                lock (_selectionSyncGate)
                {
                    path = _latestSelectionPath;
                    hwnd = _latestSelectionHwnd;
                }

                // Marked handled whether or not there is a target to send: an empty/hwnd-less request is
                // still "the newest thing we have seen", and leaving it unmarked would make the finally
                // below restart the worker forever, re-reading the same empty value every 120ms.
                Interlocked.Exchange(ref _selectionProcessed, version);

                if (!string.IsNullOrEmpty(path) && hwnd != IntPtr.Zero)
                {
                    try { ResolveAdapter(process, hwnd)?.OnSelectionChanged(hwnd, path); }
                    catch (Exception ex) { Logger.Log($"[InlineAdapterCommandHandler] OnSelectionChanged threw: {ex.Message}", LogLevel.Error); }
                }

                // Nothing newer arrived while we were working: the burst has settled.
                if (Interlocked.Read(ref _selectionVersion) == version)
                    break;
            }
        }
        finally
        {
            Interlocked.Exchange(ref _selectionWorkerRunning, 0);

            // A change can land between the loop's last read and the flag reset above; without this the
            // burst would settle with a stale highlight and no further message would arrive to fix it.
            if (Interlocked.Read(ref _selectionVersion) != Interlocked.Read(ref _selectionProcessed))
                StartSelectionWorker(process);
        }
    }

    private static void RunOnSta(Action action)
    {
        var thread = new Thread(() =>
        {
            // Belt-and-suspenders: every caller already wraps its own logic in try/catch, but an
            // exception escaping this thread's entry point entirely (e.g. from the catch block's own
            // Logger.Log call) would otherwise crash the whole process, same as any other unhandled
            // exception on a non-pooled thread.
            try { action(); }
            catch (Exception ex) { Logger.Log($"[InlineAdapterCommandHandler] STA thread threw: {ex.Message}", LogLevel.Error); }
        })
        {
            IsBackground = true,
            Name = "InlineAdapterSta"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    private static IInlineSearchAdapter? ResolveAdapter(HookProcess process, IntPtr hwnd)
    {
        if (process.ExplorerTracker != null && process.ExplorerTracker.ActiveHwnd == hwnd)
            return process.ExplorerTracker.ActiveInlineAdapter;

        var sbClass = new StringBuilder(256);
        ExplorerNativeHooks.GetClassName(hwnd, sbClass, sbClass.Capacity);
        var className = sbClass.ToString();
        var processName = "Unknown";
        try
        {
            ExplorerNativeHooks.GetWindowThreadProcessId(hwnd, out var pid);
            if (pid != 0)
            {
                using var proc = System.Diagnostics.Process.GetProcessById((int)pid);
                processName = proc.ProcessName;
            }
        }
        catch { }
        return InlineSearchAdapterRegistry.GetMatchingAdapter(hwnd, className, processName);
    }
}
