using System.Runtime.InteropServices;

namespace Lertaro.App.Services.Favorites;

/// <summary>
/// The Win32 surface the App-owned global hotkeys need: the message-only parent for the
/// <c>WM_HOTKEY</c> receiver window, plus <c>RegisterHotKey</c>/<c>UnregisterHotKey</c>.
/// </summary>
/// <remarks>
/// The App registers its own hotkeys rather than routing them through the Hook process: it already has
/// a message pump, and it already knows the foreground file-manager window it is supposed to navigate.
/// The Hook path would need a new IPC message and a second registration table for no benefit -- and the
/// Hook runs elevated, which is the wrong side to be resolving windows in.
///
/// The receiver window itself is not created here: it has to be a window WPF owns, or the hook that reads
/// <c>WM_HOTKEY</c> cannot be attached to it -- see <see cref="GlobalHotkeyWindow"/>.
/// </remarks>
internal static class FavoriteHotkeyNativeMethods
{
    public const int WmHotKey = 0x0312;

    // RegisterHotKey's modifier flags.
    public const uint ModAlt = 0x0001;
    public const uint ModControl = 0x0002;
    public const uint ModShift = 0x0004;
    public const uint ModWin = 0x0008;
    public const uint ModNoRepeat = 0x4000;

    /// <summary>
    /// HWND_MESSAGE: the pseudo-parent that makes a window message-only, so it never paints, never shows
    /// up in window enumeration or the taskbar, and still receives posted messages such as
    /// <c>WM_HOTKEY</c>.
    /// </summary>
    public static readonly IntPtr HwndMessage = new(-3);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnregisterHotKey(IntPtr hwnd, int id);

    /// <summary>A failed Win32 call's error code, for the log line explaining a refused registration.</summary>
    public static int LastError() => Marshal.GetLastWin32Error();
}
