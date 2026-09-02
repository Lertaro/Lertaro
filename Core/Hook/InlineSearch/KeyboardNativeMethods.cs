using System.Runtime.InteropServices;
using System.Text;

namespace Lertaro.Core.Hook.InlineSearch;

internal static class KeyboardNativeMethods
{
    public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
    public static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    public static extern short GetKeyState(int nVirtKey);

    // Unlike GetKeyState (per-thread synchronized state, meaningless on the hook owner's thread --
    // see the LLKHF_ALTDOWN comment below), GetAsyncKeyState reports the physical, process-global
    // key state and is the standard way for a low-level hook callback to ask about a modifier that
    // the event's own flags do not carry.
    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int nVirtKey);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    public static extern IntPtr GetKeyboardLayout(uint idThread);

    [DllImport("imm32.dll")]
    public static extern IntPtr ImmGetDefaultIMEWnd(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam, uint fuFlags, uint uTimeout, out IntPtr lpdwResult);

    [DllImport("user32.dll")]
    public static extern int ToUnicode(uint wVirtKey, uint wScanCode, byte[] lpKeyState,
        [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pwszBuff, int cchBuff, uint wFlags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetKeyboardState(byte[] lpKeyState);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GUITHREADINFO
    {
        public int cbSize;
        public uint flags;
        public IntPtr hwndActive;
        public IntPtr hwndFocus;
        public IntPtr hwndCapture;
        public IntPtr hwndMenuOwner;
        public IntPtr hwndMoveSize;
        public IntPtr hwndCaret;
        public RECT rcCaret;
    }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO lpgui);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsChild(IntPtr hWndParent, IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern IntPtr GetParent(IntPtr hWnd);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr OpenProcess(uint dwDesiredAccess, [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle, uint dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool QueryFullProcessImageName(IntPtr hProcess, uint dwFlags, [Out] StringBuilder lpExeName, ref uint lpdwSize);

    public const int WH_KEYBOARD_LL = 13;
    public const int WM_KEYDOWN = 0x0100;
    public const int WM_KEYUP = 0x0101;
    public const int WM_SYSKEYDOWN = 0x0104;
    public const int WM_SYSKEYUP = 0x0105;

    public const int VK_BACK = 0x08;
    public const int VK_ESCAPE = 0x1B;
    public const int VK_RETURN = 0x0D;
    public const int VK_TAB = 0x09;
    public const int VK_SHIFT = 0x10;
    public const int VK_CONTROL = 0x11;
    public const int VK_MENU = 0x12;
    public const int VK_SPACE = 0x20;
    public const int VK_F4 = 0x73;
    public const int VK_LWIN = 0x5B;
    public const int VK_RWIN = 0x5C;
    public const int VK_APPS = 0x5D; // The physical context-menu/"Menu" key, not VK_MENU (which is Alt).
    public const int VK_UP = 0x26;
    public const int VK_DOWN = 0x28;
    public const int VK_LEFT = 0x25;
    public const int VK_RIGHT = 0x27;
    public const int VK_PROCESSKEY = 0xE5;
    public const int VK_F10 = 0x79;

    public const uint WM_IME_CONTROL = 0x0283;
    public const uint IMC_GETOPENSTATUS = 0x0005;
    public const uint IMC_GETCONVERSIONMODE = 0x0001;
    public const int IME_CMODE_NATIVE = 0x0001;
    public const uint SMTO_ABORTIFHUNG = 0x0002;

    // GUITHREADINFO.flags bits: whether the owning thread is currently inside a menu's own modal
    // message loop (context menu, system/title-bar menu, or a submenu of either). Explorer doesn't
    // move keyboard focus to the menu HWND while it's open -- GUITHREADINFO.hwndFocus still resolves
    // to whatever control opened it -- so these flags are the only reliable way to detect it.
    public const uint GUI_INMENUMODE = 0x00000004;
    public const uint GUI_SYSTEMMENUMODE = 0x00000008;
    public const uint GUI_POPUPMENUMODE = 0x00000010;

    // KBDLLHOOKSTRUCT.flags bit: set when this key event was synthesized (SendInput/keybd_event) by
    // some process rather than coming from real hardware -- e.g. a third-party automation tool's own
    // virtual-key-based hotkey scheme, as opposed to the user's own typing.
    public const uint LLKHF_INJECTED = 0x00000010;

    // KBDLLHOOKSTRUCT.flags bit: set when Alt was held for this key event. The event carries its own
    // Alt state precisely because a low-level hook cannot ask for it: GetKeyState reports the calling
    // thread's input state, and this callback runs on the hook owner's thread, which is not the thread
    // the keystroke was headed for and has no key state of its own to report.
    public const uint LLKHF_ALTDOWN = 0x00000020;

    [StructLayout(LayoutKind.Sequential)]
    public struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }
}
