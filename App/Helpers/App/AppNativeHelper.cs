using System.Diagnostics;
using System.Text;
using Lertaro.Core.Hook;

namespace Lertaro.App.Helpers.App;

/// <summary>
/// Native window interop helpers for process and class name lookups.
/// ponytail: Split out purely to keep App.xaml.cs under the repo's 300-line limit.
/// </summary>
public static class AppNativeHelper
{
    public static string GetProcessNameOfWindow(IntPtr hwnd)
    {
        try
        {
            ExplorerNativeHooks.GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == 0) return "Unknown";
            using var process = Process.GetProcessById((int)pid);
            return process.ProcessName;
        }
        catch
        {
            return "Unknown";
        }
    }

    public static string GetClassNameOfWindow(IntPtr hwnd)
    {
        var sb = new StringBuilder(256);
        return hwnd != IntPtr.Zero && ExplorerNativeHooks.GetClassName(hwnd, sb, sb.Capacity) > 0
            ? sb.ToString()
            : "Unknown";
    }
}
