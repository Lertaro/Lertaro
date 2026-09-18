using System.Runtime.InteropServices;

namespace Lertaro.App.Services.Favorites;

/// <summary>
/// The Win32 surface the App-owned global hotkeys need: one message-only window to receive
/// <c>WM_HOTKEY</c>, plus <c>RegisterHotKey</c>/<c>UnregisterHotKey</c>.
/// </summary>
/// <remarks>
/// The App registers its own hotkeys rather than routing them through the Hook process: it already has
/// a message pump, and it already knows the foreground file-manager window it is supposed to navigate.
/// The Hook path would need a new IPC message and a second registration table for no benefit -- and the
/// Hook runs elevated, which is the wrong side to be resolving windows in.
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

    // HWND_MESSAGE: a window that never paints and is not returned by any window enumeration, which is
    // all a WM_HOTKEY receiver needs. CreateWindowExW also requires a registered class, so this uses
    // the system's own "STATIC" class rather than registering one -- no WndProc to keep alive, no
    // module handle to resolve, and nothing registered process-wide that a crash could leave behind.
    public static readonly IntPtr HwndMessage = new(-3);
    private const string MessageOnlyClass = "STATIC";

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr CreateWindowExW(
        uint exStyle, string className, string? windowName, uint style,
        int x, int y, int width, int height,
        IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnregisterHotKey(IntPtr hwnd, int id);

    /// <summary>
    /// Creates the message-only window. Returns <see cref="IntPtr.Zero"/> when the OS refuses (rather
    /// than throwing), so the caller can degrade to "no per-favorite hotkeys" with one log line.
    /// </summary>
    public static IntPtr CreateMessageWindow()
    {
        try
        {
            return CreateWindowExW(0, MessageOnlyClass, null, 0, 0, 0, 0, 0,
                HwndMessage, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        }
        catch (EntryPointNotFoundException)
        {
            return IntPtr.Zero;
        }
    }

    /// <summary>A failed Win32 call's error code, for the log line explaining a refused registration.</summary>
    public static int LastError() => Marshal.GetLastWin32Error();
}
