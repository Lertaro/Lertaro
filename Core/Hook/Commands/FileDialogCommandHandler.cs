using System.Text;
using Lertaro.PluginSdk.Registries;

using Lertaro.Core.Wire;
using Lertaro.PluginSdk.Abstractions.Plugins.WindowAdapters;
using Lertaro.Core.Hook.Ipc;
namespace Lertaro.Core.Hook.Commands;

// Split out of HookCommandHandler to keep that file under the line-count limit. Handles
// NavigateDialog/RestoreDialogFocus, dispatching to IFileDialogAdapter in the Hook process. Also
// deduplicates the resolve-adapter-from-hwnd fallback the two commands previously each had their own copy of.
internal static class FileDialogCommandHandler
{
    public static void HandleNavigateDialog(HookProcess process, IpcMessage msg)
    {
        var dialogHwnd = (IntPtr)msg.Hwnd;
        var navPath = msg.StringVal1;
        if (dialogHwnd == IntPtr.Zero || string.IsNullOrEmpty(navPath)) return;

        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                // Resolving a favorite on a disconnected drive can take time. Only hand focus back
                // while the dialog (or its owned inline card) is still in front, never over another app.
                if (ResolveAdapter(process, dialogHwnd) is { } adapter)
                {
                    if (msg.BoolVal && !IsForegroundDialogOrOwnedWindow(dialogHwnd)) return;
                    Navigate(adapter, dialogHwnd, navPath, msg.BoolVal);
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"[FileDialogCommandHandler] NavigateTo threw: {ex.Message}", LogLevel.Error);
            }
        });
    }

    private static bool IsForegroundDialogOrOwnedWindow(IntPtr dialogHwnd)
    {
        var foreground = ExplorerNativeHooks.GetForegroundWindow();
        while (foreground != IntPtr.Zero)
        {
            if (foreground == dialogHwnd) return true;
            foreground = ExplorerNativeHooks.GetParent(foreground);
        }
        return false;
    }

    // A favorite can be invoked while our inline card owns the foreground. Restore focus in the
    // same work item, before filling the path: separate IPC requests would race on pool threads.
    // The adapter's existing foreground check still decides whether its delayed Enter is safe.
    internal static bool Navigate(IFileDialogAdapter adapter, IntPtr dialogHwnd, string path, bool restoreFocus,
        Func<IntPtr>? foregroundWindow = null)
    {
        if (restoreFocus && (!adapter.RestoreFocus(dialogHwnd)
            || (foregroundWindow ?? ExplorerNativeHooks.GetForegroundWindow)() != dialogHwnd)) return false;
        return adapter.NavigateTo(dialogHwnd, path);
    }

    /// <summary>
    /// The same navigation, off the caller's thread and never throwing into it: NavigateTo sends messages into
    /// another process, so the tracker thread that owns the state cannot be the one that waits for it, and an
    /// adapter fault must not escape a pool thread (which takes the whole hook process -- and every global
    /// hotkey -- down with it).
    /// </summary>
    /// <remarks>
    /// The adapter is resolved HERE, from the handle, rather than handed in from the caller's snapshot: see
    /// <see cref="ResolveAdapter(ExplorerTracker?, IntPtr)"/>.
    /// </remarks>
    internal static void NavigateOffThread(ExplorerTracker? tracker, IntPtr dialogHwnd, string path, bool restoreFocus = false) =>
        ThreadPool.QueueUserWorkItem(_ => NavigateResolvingAdapter(tracker, dialogHwnd, path, restoreFocus));

    /// <summary>Resolves the adapter for the window that is actually being navigated, then navigates.</summary>
    internal static void NavigateResolvingAdapter(ExplorerTracker? tracker, IntPtr dialogHwnd, string path, bool restoreFocus = false)
    {
        IFileDialogAdapter? adapter;
        try
        {
            adapter = ResolveAdapter(tracker, dialogHwnd);
        }
        catch (Exception ex)
        {
            Logger.Log($"[FileDialogCommandHandler] Resolving an adapter for 0x{dialogHwnd:x} threw: {ex.Message}", LogLevel.Error);
            return;
        }

        if (adapter == null)
        {
            Logger.Log($"[FileDialogCommandHandler] No adapter matched 0x{dialogHwnd:x}; it cannot be navigated to '{path}'.", LogLevel.Warn);
            return;
        }

        NavigateLoggingFailures(adapter, dialogHwnd, path, restoreFocus);
    }

    /// <summary>The work item's body, callable directly so its two failure modes can be exercised.</summary>
    internal static void NavigateLoggingFailures(IFileDialogAdapter adapter, IntPtr dialogHwnd, string path, bool restoreFocus = false)
    {
        try
        {
            if (!Navigate(adapter, dialogHwnd, path, restoreFocus))
                Logger.Log($"[FileDialogCommandHandler] Adapter {adapter.GetType().Name} declined to navigate 0x{dialogHwnd:x} to '{path}'.", LogLevel.Warn);
        }
        catch (Exception ex)
        {
            Logger.Log($"[FileDialogCommandHandler] NavigateTo threw for 0x{dialogHwnd:x}: {ex.Message}", LogLevel.Error);
        }
    }

    public static void HandleRestoreDialogFocus(HookProcess process, IpcMessage msg)
    {
        var activeHwnd = (IntPtr)msg.Hwnd;
        if (activeHwnd == IntPtr.Zero) return;

        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                ResolveAdapter(process, activeHwnd)?.RestoreFocus(activeHwnd);
            }
            catch (Exception ex)
            {
                Logger.Log($"[FileDialogCommandHandler] RestoreFocus threw: {ex.Message}", LogLevel.Error);
            }
        });
    }

    /// <summary>
    /// The adapter for <paramref name="hwnd"/> as it is right now.
    /// </summary>
    /// <remarks>
    /// The registry is asked FIRST, by handle, and the tracker's cached adapter is only the fallback. The
    /// cached one was resolved at the instant the window was classified, and the tracker deliberately
    /// re-derives it afterwards: two adapters divide the common dialogs on a child window that is built after
    /// the dialog appears, so the adapter that won the first frame can be the wrong one for the window in
    /// front of the user now. Handing that one to NavigateTo gets a silent false -- the tracker's own
    /// re-derivation exists for the same reason (PR #352).
    /// </remarks>
    internal static IFileDialogAdapter? ResolveAdapter(ExplorerTracker? tracker, IntPtr hwnd)
    {
        var matched = MatchByHandle(hwnd);
        if (matched != null) return matched;

        return tracker != null && tracker.ActiveHwnd == hwnd ? tracker.ActiveAdapter : null;
    }

    private static IFileDialogAdapter? MatchByHandle(IntPtr hwnd)
    {
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
        return FileDialogAdapterRegistry.GetMatchingAdapter(hwnd, className, processName);
    }

    private static IFileDialogAdapter? ResolveAdapter(HookProcess process, IntPtr hwnd) => ResolveAdapter(process.ExplorerTracker, hwnd);
}
