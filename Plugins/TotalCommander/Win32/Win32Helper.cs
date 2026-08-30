using System.Runtime.InteropServices;
using System.Text;

namespace Lertaro.Plugins.TotalCommander.Win32;

public static class Win32Helper
{
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO lpgui);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GUITHREADINFO
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

    public static string GetClassName(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero) return string.Empty;
        var sb = new StringBuilder(256);
        GetClassName(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }

    /// <summary>The keyboard-focused control on the thread that owns <paramref name="hWnd"/> (the active file list).</summary>
    public static IntPtr GetFocusedControl(IntPtr hWnd)
    {
        var threadId = GetWindowThreadProcessId(hWnd, out _);
        if (threadId == 0) return IntPtr.Zero;
        var gui = new GUITHREADINFO { cbSize = Marshal.SizeOf<GUITHREADINFO>() };
        return GetGUIThreadInfo(threadId, ref gui) ? gui.hwndFocus : IntPtr.Zero;
    }

    // ---- Total Commander WM_COPYDATA remote-control protocol (documented on the ghisler.ch forum) ----

    private const uint WM_COPYDATA = 0x004A;

    // Query commands (dwData = op + 256*encoding). We use the wide/Unicode variants so non-ASCII paths survive.
    private const int GET_REQUEST_W = 'G' + 256 * 'W';  // send: ask for a value, reply comes back as UTF-16
    private const int GET_REPLY_W = 'R' + 256 * 'W';    // receive: TC's answer

    // Change-directory command (dwData 17475). lpData layout:
    //   [UTF-8 BOM] leftPath 0x0D [UTF-8 BOM] rightPath 0x00 <flags> 0x00
    // Flags: 'S' = treat the two slots as source/target (active pane) rather than left/right; 'A' = don't
    // enter archives, open the parent folder and put the cursor on the item. The trailing 0x00 after the
    // flags MUST be counted in cbData or TC parses the flags unreliably.
    private const int CHANGE_DIR = 'C' + 256 * 'D';     // 17475

    [StructLayout(LayoutKind.Sequential)]
    private struct COPYDATASTRUCT
    {
        public IntPtr dwData;
        public int cbData;
        public IntPtr lpData;
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, ref COPYDATASTRUCT lParam);

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASS
    {
        public uint style;
        public WndProcDelegate lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpszClassName;
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassW(ref WNDCLASS lpWndClass);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowExW(uint dwExStyle, string lpClassName, string? lpWindowName, uint dwStyle,
        int x, int y, int w, int h, IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ChangeWindowMessageFilterEx(IntPtr hWnd, uint msg, uint action, IntPtr changeInfo);

    private const uint MSGFLT_ALLOW = 1;
    private static readonly IntPtr HWND_MESSAGE = new(-3);
    private const string QueryClassName = "LertaroTcQueryWindow";

    private static readonly object _classLock = new();
    private static bool _classRegistered;
    private static WndProcDelegate? _wndProc; // kept alive so the native side never calls a collected delegate

    [ThreadStatic] private static string? _capturedPath;

    private static void EnsureClassRegistered()
    {
        lock (_classLock)
        {
            if (_classRegistered) return;
            _wndProc = QueryWndProc;
            var wc = new WNDCLASS
            {
                lpfnWndProc = _wndProc,
                hInstance = GetModuleHandle(null),
                lpszClassName = QueryClassName
            };
            RegisterClassW(ref wc);
            _classRegistered = true;
        }
    }

    private static IntPtr QueryWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_COPYDATA)
        {
            var cds = Marshal.PtrToStructure<COPYDATASTRUCT>(lParam);
            if (cds.dwData == (IntPtr)GET_REPLY_W && cds.lpData != IntPtr.Zero && cds.cbData > 0)
            {
                _capturedPath = Marshal.PtrToStringUni(cds.lpData, cds.cbData / 2)?.TrimEnd('\0');
            }
            return (IntPtr)1;
        }
        return DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    /// <summary>
    /// Asks Total Commander for the current directory of its source (active) panel via WM_COPYDATA.
    /// TC replies with a WM_COPYDATA back to a throwaway message-only window we create for this call; because
    /// we are blocked inside SendMessage the reply is delivered synchronously (nested) before it returns.
    /// </summary>
    public static string? QuerySourcePanelPath(IntPtr tcHwnd) => QueryPanelPath(tcHwnd, "SP");

    /// <summary>
    /// Asks Total Commander for the current directory of its target panel via WM_COPYDATA.
    /// </summary>
    public static string? QueryTargetPanelPath(IntPtr tcHwnd) => QueryPanelPath(tcHwnd, "TP");

    private static string? QueryPanelPath(IntPtr tcHwnd, string command)
        => StaQueryHelper.Run(() => QueryPanelPathCore(tcHwnd, command), null);

    private static string? QueryPanelPathCore(IntPtr tcHwnd, string command)
    {
        if (tcHwnd == IntPtr.Zero) return null;
        EnsureClassRegistered();

        var receiver = CreateWindowExW(0, QueryClassName, null, 0, 0, 0, 0, 0, HWND_MESSAGE, IntPtr.Zero, GetModuleHandle(null), IntPtr.Zero);
        if (receiver == IntPtr.Zero) return null;

        // We run inside the elevated hook service, but TC is a normal-integrity process. Without this, UIPI
        // silently drops TC's WM_COPYDATA reply to our higher-integrity window and the path comes back null.
        ChangeWindowMessageFilterEx(receiver, WM_COPYDATA, MSGFLT_ALLOW, IntPtr.Zero);

        try
        {
            _capturedPath = null;
            // The command string is ALWAYS ANSI ("SP"/"TP" = Source/target panel path); the 'W' in GET_REQUEST_W only
            // asks TC to return the answer as UTF-16. Sending the command itself as UTF-16 yields no reply.
            var cmd = Encoding.ASCII.GetBytes(command + "\0");
            var pin = GCHandle.Alloc(cmd, GCHandleType.Pinned);
            try
            {
                var cds = new COPYDATASTRUCT
                {
                    dwData = (IntPtr)GET_REQUEST_W,
                    cbData = cmd.Length,
                    lpData = pin.AddrOfPinnedObject()
                };
                SendMessage(tcHwnd, WM_COPYDATA, receiver, ref cds);
            }
            finally
            {
                pin.Free();
            }
            return string.IsNullOrEmpty(_capturedPath) ? null : _capturedPath;
        }
        finally
        {
            DestroyWindow(receiver);
        }
    }

    /// <summary>
    /// Navigates Total Commander's source (active) panel via the CD WM_COPYDATA command. The 'S' flag makes
    /// the path apply to the active pane rather than the physical left panel; when <paramref name="placeCursorOnItem"/>
    /// is set (for a file) the 'A' flag opens the parent folder and puts the cursor on the item instead of
    /// trying to enter it. The other pane is left empty (unchanged). Paths go out as UTF-8 (BOM-prefixed).
    /// </summary>
    public static bool ChangeSourcePanelDirectory(IntPtr tcHwnd, string path, bool placeCursorOnItem)
    {
        if (tcHwnd == IntPtr.Zero || string.IsNullOrEmpty(path)) return false;

        var flags = placeCursorOnItem ? "SA" : "S";
        var buf = new List<byte>();
        buf.AddRange(new byte[] { 0xEF, 0xBB, 0xBF });    // UTF-8 BOM -> interpret the path as UTF-8
        buf.AddRange(Encoding.UTF8.GetBytes(path));       // source (active) pane path
        buf.Add(0x0D);                                    // separator; target (other) pane left empty = unchanged
        buf.Add(0x00);                                    // end of path section
        buf.AddRange(Encoding.ASCII.GetBytes(flags));     // 'S' [+ 'A']
        buf.Add(0x00);                                    // final NUL -- must be included in cbData
        var bytes = buf.ToArray();

        // TC only acts on the CD command while it is the foreground window. We are called from the inline
        // window, which is currently foreground, so we can hand the foreground to TC; then send the command
        // after a short settle on a background thread so the UI thread isn't blocked.
        SetForegroundWindow(tcHwnd);
        Task.Run(() =>
        {
            Thread.Sleep(150);
            var pin = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            try
            {
                var cds = new COPYDATASTRUCT
                {
                    dwData = (IntPtr)CHANGE_DIR,
                    cbData = bytes.Length,
                    lpData = pin.AddrOfPinnedObject()
                };
                SendMessage(tcHwnd, WM_COPYDATA, IntPtr.Zero, ref cds);
            }
            finally
            {
                pin.Free();
            }
        });
        return true;
    }
}
