using System.Runtime.InteropServices;

namespace Lertaro.Plugins.WPS.Interop;

/// <summary>
/// The Win32 half of driving WPS's file dialog: window geometry, foreground activation, and the Enter
/// keystroke that commits the path. Everything that reaches inside the dialog goes through UI Automation
/// instead (see <see cref="WPSDialogAutomation"/>) -- the dialog is Qt, so its controls are not child
/// HWNDs and none of this can address them.
/// </summary>
internal static class WPSWindowInterop
{
    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const ushort VK_RETURN = 0x0D;
    private const int ForegroundPollMs = 20;

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    // The union has to carry MOUSEINPUT even though only the keyboard arm is ever used: SendInput
    // validates cbSize against the full INPUT size, and a keyboard-only declaration is smaller than the
    // mouse arm, so it would be rejected outright rather than merely doing the wrong thing.
    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }

    internal static bool IsAlive(IntPtr hwnd) => hwnd != IntPtr.Zero && IsWindow(hwnd);

    /// <summary>
    /// Alive and still on screen. A dialog being dismissed is hidden before it is destroyed, so this
    /// turns false earlier than <see cref="IsAlive"/> does -- which is the point: it is the last cheap
    /// chance to notice that a window is on its way out before doing anything to it.
    /// </summary>
    internal static bool IsLiveAndVisible(IntPtr hwnd) => IsAlive(hwnd) && IsWindowVisible(hwnd);

    /// <summary>
    /// The dialog's bounds. Prefers the DWM extended frame bounds over GetWindowRect for the same reason
    /// the other dialog adapters do: GetWindowRect includes the invisible resize border, which puts the
    /// docked search window a few pixels off on every edge.
    /// </summary>
    internal static bool TryGetDialogRect(IntPtr hwnd, out RECT rect)
    {
        rect = default;
        if (!IsAlive(hwnd))
            return false;

        if (DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out rect, Marshal.SizeOf<RECT>()) == 0)
            return true;

        return GetWindowRect(hwnd, out rect);
    }

    /// <summary>Brings the window to the foreground without waiting to see whether it worked.</summary>
    internal static bool Activate(IntPtr hwnd) => IsAlive(hwnd) && SetForegroundWindow(hwnd);

    /// <summary>
    /// Brings the dialog to the foreground and waits until it actually is, up to
    /// <paramref name="timeoutMs"/>.
    /// </summary>
    /// <remarks>
    /// SetForegroundWindow returning true is not proof of anything: Windows refuses foreground changes
    /// from a process that does not currently own it, and does so silently. This matters here more than
    /// in the Win32 adapters, because what follows is a synthesised keystroke that goes to whatever holds
    /// focus at the time -- committing the path into some other application's window if the activation
    /// quietly did not happen. Confirmed necessary by the AutoHotkey implementation this was rebuilt
    /// against, which waits on WinWaitActive and treats a still-inactive dialog as a failure.
    /// </remarks>
    internal static bool ActivateAndWait(IntPtr hwnd, int timeoutMs)
    {
        if (!IsAlive(hwnd))
            return false;

        if (GetForegroundWindow() == hwnd)
            return true;

        SetForegroundWindow(hwnd);

        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            if (GetForegroundWindow() == hwnd)
                return true;
            Thread.Sleep(ForegroundPollMs);
        }

        return GetForegroundWindow() == hwnd;
    }

    /// <summary>
    /// Sends Enter to whatever currently has keyboard focus.
    /// </summary>
    /// <remarks>
    /// Synthesised at the input level rather than posted as a WM_KEYDOWN to a control, because there is no
    /// control HWND to post to -- the file-name editor is a Qt widget drawn inside the dialog's single
    /// native window. The caller is responsible for having focused that editor first (UI Automation's
    /// SetFocus does that), since this goes wherever focus already is.
    /// </remarks>
    internal static bool SendEnter()
    {
        var inputs = new INPUT[2];
        inputs[0].type = INPUT_KEYBOARD;
        inputs[0].U.ki.wVk = VK_RETURN;
        inputs[1].type = INPUT_KEYBOARD;
        inputs[1].U.ki.wVk = VK_RETURN;
        inputs[1].U.ki.dwFlags = KEYEVENTF_KEYUP;

        return SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>()) == inputs.Length;
    }
}
