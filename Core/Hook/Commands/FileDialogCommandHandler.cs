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

    private static IFileDialogAdapter? ResolveAdapter(HookProcess process, IntPtr hwnd)
    {
        if (process.ExplorerTracker != null && process.ExplorerTracker.ActiveHwnd == hwnd)
            return process.ExplorerTracker.ActiveAdapter;

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
}
