using System.Runtime.InteropServices;

namespace Lertaro.Core.Hook.InlineSearch;

public class MouseHookService : IDisposable
{
    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    private const int WH_MOUSE_LL = 14;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_RBUTTONDOWN = 0x0204;
    private const int WM_MBUTTONDOWN = 0x0207;

    private const int SM_CXDOUBLECLK = 36;
    private const int SM_CYDOUBLECLK = 37;

    [DllImport("user32.dll")]
    private static extern uint GetDoubleClickTime();

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    private IntPtr _hookId = IntPtr.Zero;
    private LowLevelMouseProc? _proc;

    private int _lastClickTime;
    private int _lastClickX;
    private int _lastClickY;

    public event Action<int, int>? OnMouseClick;
    public event Action<int, int>? OnMouseDoubleClick;
    public event Action<int, int>? OnMouseMiddleClick;
    // Separate from OnMouseClick (which also fires for this same button, for the existing
    // click-outside-closes-menu wiring) -- carries the raw hook timestamp so KeyboardHookService can
    // measure real elapsed time against a keystroke's own timestamp, not our processing latency.
    public event Action<uint>? OnRightButtonDown;

    public void Start()
    {
        if (_hookId != IntPtr.Zero) return;
        _proc = HookCallback;
        var hMod = GetModuleHandle(null);
        _hookId = SetWindowsHookEx(WH_MOUSE_LL, _proc, hMod, 0);
        if (_hookId == IntPtr.Zero)
        {
            Logger.Log($"[MouseHookService] Failed to install mouse hook! Error={Marshal.GetLastWin32Error()}", LogLevel.Error);
        }
    }

    public void Stop()
    {
        if (_hookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        // Catch-all around the whole callback: an exception escaping a native low-level hook callback
        // terminates the process (taking every global hook down with it), and the handlers invoked
        // here include disk/IPC-adjacent work. Log and keep the hook chain flowing either way.
        try
        {
            if (nCode >= 0)
            {
                if (wParam == (IntPtr)WM_LBUTTONDOWN)
                {
                    var hookStruct = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                    var clickTime = (int)hookStruct.time;
                    var doubleClickTime = (int)GetDoubleClickTime();
                    var dx = Math.Abs(hookStruct.pt.x - _lastClickX);
                    var dy = Math.Abs(hookStruct.pt.y - _lastClickY);

                    if (clickTime - _lastClickTime <= doubleClickTime &&
                        dx <= GetSystemMetrics(SM_CXDOUBLECLK) &&
                        dy <= GetSystemMetrics(SM_CYDOUBLECLK))
                    {
                        OnMouseDoubleClick?.Invoke(hookStruct.pt.x, hookStruct.pt.y);
                    }

                    _lastClickTime = clickTime;
                    _lastClickX = hookStruct.pt.x;
                    _lastClickY = hookStruct.pt.y;
                    OnMouseClick?.Invoke(hookStruct.pt.x, hookStruct.pt.y);
                }
                else if (wParam == (IntPtr)WM_MBUTTONDOWN)
                {
                    var hookStruct = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                    OnMouseMiddleClick?.Invoke(hookStruct.pt.x, hookStruct.pt.y);
                }
                else if (wParam == (IntPtr)WM_RBUTTONDOWN)
                {
                    var hookStruct = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                    OnRightButtonDown?.Invoke(hookStruct.time);
                    OnMouseClick?.Invoke(hookStruct.pt.x, hookStruct.pt.y);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"[MouseHookService] Hook callback error: {ex.Message}", LogLevel.Error);
        }

        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    public void Dispose() => Stop();
}
