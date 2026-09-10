using System.Text;
using Lertaro.PluginSdk.Registries;

namespace Lertaro.Core.Hook;

// Split out purely to keep ExplorerWindowClassifier under the repo's per-file line limit. This class
// contains the focus-event filter and reads only the state of the one tracker passed to it.
internal static class ExplorerFocusChangeFilter
{
    public static bool IsIgnored(ExplorerTracker tracker, IntPtr hwnd)
    {
        var sbClass = new StringBuilder(256);
        ExplorerNativeHooks.GetClassName(hwnd, sbClass, sbClass.Capacity);
        var className = sbClass.ToString();
        if (className.Contains("InputSwitch", StringComparison.OrdinalIgnoreCase)) return true;
        ExplorerNativeHooks.GetWindowThreadProcessId(hwnd, out var activePid);
        if (activePid == Environment.ProcessId || (activePid != 0 && activePid == tracker.AppProcessId))
        {
            if (className.Equals("#32770", StringComparison.OrdinalIgnoreCase)) return false;
            var rootHwnd = ExplorerNativeHooks.GetAncestor(hwnd, ExplorerNativeHooks.GA_ROOTOWNER);
            if (rootHwnd == IntPtr.Zero) rootHwnd = hwnd;
            var processName = tracker.GetProcessName(rootHwnd);
            if (ActivePathCollectorRegistry.GetCollectors()
                .Any(collector => collector.CanHandle(rootHwnd, className, processName))) return false;
            return true;
        }
        if (tracker.ActiveHwnd != IntPtr.Zero)
        {
            var rootHwnd = ExplorerNativeHooks.GetAncestor(hwnd, ExplorerNativeHooks.GA_ROOTOWNER);
            if (rootHwnd == IntPtr.Zero) rootHwnd = hwnd;
            if (rootHwnd == tracker.ActiveHwnd) return true;
        }
        return false;
    }
}
