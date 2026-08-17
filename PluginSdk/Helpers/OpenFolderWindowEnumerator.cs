using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Lertaro.PluginSdk.Helpers;

/// <summary>
/// Enumerates visible top-level windows for opened-folder collectors.
/// </summary>
public static class OpenFolderWindowEnumerator
{
    private delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    /// <summary>
    /// Gets visible top-level windows matching <paramref name="matches"/>.
    /// </summary>
    public static IReadOnlyList<IntPtr> FindVisibleWindows(Func<IntPtr, bool> matches)
    {
        var windows = new List<IntPtr>();
        EnumWindows((window, _) =>
        {
            try
            {
                if (IsWindowVisible(window) && matches(window))
                    windows.Add(window);
            }
            catch
            {
                // One inaccessible window must not prevent other file managers from contributing.
            }
            return true;
        }, IntPtr.Zero);
        return windows;
    }

    /// <summary>
    /// Gets the owning process name without its executable extension.
    /// </summary>
    public static string GetProcessName(IntPtr window)
    {
        GetWindowThreadProcessId(window, out var processId);
        if (processId == 0) return string.Empty;
        try
        {
            using var process = Process.GetProcessById((int)processId);
            return process.ProcessName;
        }
        catch
        {
            return string.Empty;
        }
    }
}
