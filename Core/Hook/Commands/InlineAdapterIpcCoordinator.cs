using Lertaro.Core.Wire;
using Lertaro.PluginSdk.Abstractions.Plugins.WindowAdapters;
using Lertaro.PluginSdk.Helpers;
using Lertaro.PluginSdk.Registries;
using System.Text;
namespace Lertaro.Core.Hook.Commands;

// Correlates the App-side blocking ExecuteItem call with the Hook's async reply over the event pipe.
public static class InlineAdapterIpcCoordinator
{
    private static readonly object _lock = new();
    private static AutoResetEvent? _executeItemEvent;
    private static bool _executeItemResult;
    // Echoed back on the response and checked in SetExecuteItemResult so a reply that arrives after its
    // own call already gave up on WaitOne (timed out) can't be misattributed to a later, unrelated call --
    // both share the same static fields since only one ExecuteItem can be in flight at a time (the lock).
    private static int _pendingRequestId;
    private static int _nextRequestId;

    // Limit the UI-facing IPC wait to one second. A false return means success was not confirmed within
    // that wait; the Hook operation may still finish. Callers considering a fallback launch should await
    // lateResult off the UI thread first to avoid executing the same item twice.
    //
    // Encode the caller's known isDir value as a trailing separator. An elevated Hook may not see network
    // drive letters mapped in the caller's session, so adapters cannot reliably infer it with File.Exists
    // or Directory.Exists. The separator carries this information without changing the IPC interface.
    public static bool ExecuteItem(IntPtr hwnd, string path, bool isDir, string searchInput, Action<IpcMessage> sendMsg, out Task<bool> lateResult)
        => SendItem(IpcMessageId.ExecuteInlineItem, hwnd, path, isDir, searchInput, sendMsg, out lateResult);

    public static bool LocateItem(IntPtr hwnd, string path, bool isDir, Action<IpcMessage> sendMsg, out Task<bool> lateResult)
        => SendItem(IpcMessageId.LocateInlineItem, hwnd, path, isDir, string.Empty, sendMsg, out lateResult);

    internal static bool SendItem(IpcMessageId command, IntPtr hwnd, string path, bool isDir, string searchInput,
        Action<IpcMessage> sendMsg, out Task<bool> lateResult, IReadOnlyList<IInlineSearchAdapter>? adapters = null)
    {
        var normalizedPath = NormalizePath(path, isDir);

        foreach (var adapter in adapters ?? InlineSearchAdapterRegistry.GetAdapters())
        {
            try
            {
                if (!adapter.NavigateInUserApp) continue;
                var windowClass = new StringBuilder(256);
                ExplorerNativeHooks.GetClassName(hwnd, windowClass, windowClass.Capacity);
                if (!adapter.CanHandle(hwnd, windowClass.ToString(), OpenFolderWindowEnumerator.GetProcessName(hwnd))) continue;
            }
            catch (Exception ex)
            {
                Logger.Log($"[Inline navigation] Matching {adapter.GetType().Name}: {ex.Message}", LogLevel.Warn);
                continue;
            }

            lateResult = Task.Run(() =>
            {
                try
                {
                    return command == IpcMessageId.LocateInlineItem ? adapter.LocateItem(hwnd, normalizedPath)
                        : adapter.ExecuteItem(hwnd, normalizedPath, searchInput);
                }
                catch (Exception ex)
                {
                    Logger.Log($"[Inline navigation] {adapter.GetType().Name}: {ex.Message}", LogLevel.Warn);
                    return false;
                }
            });
            return false;
        }

        lock (_lock)
        {
            var evt = new AutoResetEvent(false);
            var requestId = ++_nextRequestId;
            _pendingRequestId = requestId;
            _executeItemEvent = evt;
            _executeItemResult = false;

            sendMsg(new IpcMessage { Id = command, Hwnd = hwnd.ToInt64(), StringVal1 = normalizedPath, StringVal2 = searchInput, IntVal = requestId });

            if (evt.WaitOne(1000))
            {
                lateResult = Task.FromResult(_executeItemResult);
                evt.Dispose();
                return _executeItemResult;
            }

            // Handed off to a background wait rather than disposed here -- SetExecuteItemResult may still
            // set it from the IPC receive thread once the slow call actually finishes.
            lateResult = Task.Run(() =>
            {
                try
                {
                    return evt.WaitOne(4000) && requestId == _pendingRequestId && _executeItemResult;
                }
                finally
                {
                    evt.Dispose();
                }
            });
            return false;
        }
    }

    // Path.EndsInDirectorySeparator/TrimEndingDirectorySeparator, not a manual TrimEnd -- a bare TrimEnd
    // would eat a drive/UNC root's own separator too (e.g. "C:\" -> "C:", which no longer means the root),
    // where these two treat the root's separator as non-optional.
    private static string NormalizePath(string path, bool isDir)
    {
        if (isDir)
            return Path.EndsInDirectorySeparator(path) ? path : path + "\\";
        return Path.TrimEndingDirectorySeparator(path);
    }

    public static void SetExecuteItemResult(int requestId, bool result)
    {
        if (requestId != _pendingRequestId) return; // stale reply for a call we already gave up on
        _executeItemResult = result;
        try { _executeItemEvent?.Set(); } catch { }
    }

    /// <summary>
    /// Waits on the <paramref name="lateResult"/> task from a timed-out <see cref="ExecuteItem"/> call and
    /// runs exactly one of the two continuations depending on whether the in-flight Hook-side call
    /// eventually confirmed success -- shared by every caller (Quick Navigation menu, inline search's own
    /// Enter-to-execute) that would otherwise need its own copy of "wait a bit longer, off the UI thread,
    /// before treating a timeout as a real failure and running some other fallback".
    /// </summary>
    public static async Task RunAfterLateResultAsync(Task<bool> lateResult, Action onSuccess, Action onFallback)
    {
        if (await lateResult) onSuccess(); else onFallback();
    }
}
