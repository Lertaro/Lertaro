using System.Runtime.InteropServices;
using Lertaro.Core.Wire;

namespace Lertaro.App.Views.QuickSearchWindow.Helpers;

/// <summary>Win32 calls shared by quick-window activation and visibility flows.</summary>
internal static class QuickSearchWindowNative
{
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    internal static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    internal const int RestoreWindow = 9;

    // The hook process completes foreground activation asynchronously, so every caller keeps the
    // existing IPC handoff instead of assuming the local Win32 call is sufficient.
    internal static void ForceForeground(IntPtr hwnd, bool useAltTapBypass = true)
    {
        if (hwnd == IntPtr.Zero) return;
        ShowWindow(hwnd, RestoreWindow);
        App.HookClient?.SendMessage(new IpcMessage
        {
            Id = IpcMessageId.ForceForeground,
            Hwnd = hwnd.ToInt64(),
            BoolVal = useAltTapBypass
        });
    }
}
