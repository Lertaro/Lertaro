using System.Runtime.InteropServices;
using System.Text;
namespace Lertaro.Plugins.Xyplorer.Win32;

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
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    public static string GetClassName(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero) return string.Empty;
        var sb = new StringBuilder(256);
        GetClassName(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }

    /// <summary>The keyboard-focused control on the thread that owns <paramref name="hWnd"/> (the active pane).</summary>
    public static IntPtr GetFocusedControl(IntPtr hWnd)
    {
        var threadId = GetWindowThreadProcessId(hWnd, out _);
        if (threadId == 0) return IntPtr.Zero;
        var gui = new GUITHREADINFO { cbSize = Marshal.SizeOf<GUITHREADINFO>() };
        return GetGUIThreadInfo(threadId, ref gui) ? gui.hwndFocus : IntPtr.Zero;
    }

    // ---- XYplorer WM_COPYDATA remote-control protocol (documented on the XYplorer Beta Club forum) ----
    //
    // Unlike Total Commander's dedicated GET/CD wire commands, XYplorer exposes a single primitive: run an
    // arbitrary XYplorer script. A fire-and-forget command (navigation) is just sent directly; a query has
    // the script itself call XYplorer's own `copydata` script command to relay the answer back to a
    // throwaway message-only window we create for the call, via a *second*, inbound WM_COPYDATA -- the same
    // "create a receiver, block inside SendMessage, read the nested reply" shape as the Total Commander
    // adapter's Win32Helper, just built on XYplorer's script engine instead of a wire protocol.

    private const uint WM_COPYDATA = 0x004A;

    // dwData for an outbound WM_COPYDATA to XYplorer: run the message text as an XYplorer script immediately.
    private const int RUN_SCRIPT = 0x00400001;

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
    private const string QueryClassName = "LertaroXyQueryWindow";

    private static readonly object _classLock = new();
    private static bool _classRegistered;
    private static WndProcDelegate? _wndProc; // kept alive so the native side never calls a collected delegate

    [ThreadStatic] private static string? _capturedReply;

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
            if (cds.lpData != IntPtr.Zero && cds.cbData > 0)
            {
                _capturedReply = Marshal.PtrToStringUni(cds.lpData, cds.cbData / 2)?.TrimEnd('\0');
            }
            return (IntPtr)1;
        }
        return DefWindowProcW(hWnd, msg, wParam, lParam);
    }

    /// <summary>
    /// Runs an XYplorer script that resolves <paramref name="quotedExpression"/> (a quoted XYplorer script
    /// string, e.g. <c>"&lt;curpath&gt;"</c>) and relays the result back to a throwaway message-only window
    /// via XYplorer's own <c>copydata</c> script command, so the value comes back synchronously (nested)
    /// inside SendMessage before it returns.
    /// </summary>
    public static string? QueryExpression(IntPtr xyHwnd, string quotedExpression)
        => StaQueryHelper.Run(() => QueryExpressionCore(xyHwnd, quotedExpression), null);

    private static string? QueryExpressionCore(IntPtr xyHwnd, string quotedExpression)
    {
        if (xyHwnd == IntPtr.Zero) return null;
        EnsureClassRegistered();

        var receiver = CreateWindowExW(0, QueryClassName, null, 0, 0, 0, 0, 0, HWND_MESSAGE, IntPtr.Zero, GetModuleHandle(null), IntPtr.Zero);
        if (receiver == IntPtr.Zero) return null;

        // We run inside the elevated hook service, but XYplorer is a normal-integrity process. Without this,
        // UIPI silently drops XYplorer's WM_COPYDATA reply to our higher-integrity window and the value comes
        // back null (see the identical fix in the Total Commander adapter's Win32Helper).
        ChangeWindowMessageFilterEx(receiver, WM_COPYDATA, MSGFLT_ALLOW, IntPtr.Zero);

        try
        {
            _capturedReply = null;
            // $return is a scratch script variable name; "$return" inside the quoted copydata argument is
            // interpolated by XYplorer's own string substitution, not passed as the literal text "$return".
            var script = $"::$return = {quotedExpression}; copydata {(long)receiver}, \"$return\", 2;";
            var bytes = Encoding.Unicode.GetBytes(script);
            var pin = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            try
            {
                var cds = new COPYDATASTRUCT
                {
                    dwData = (IntPtr)RUN_SCRIPT,
                    cbData = bytes.Length,
                    lpData = pin.AddrOfPinnedObject()
                };
                SendMessage(xyHwnd, WM_COPYDATA, receiver, ref cds);
            }
            finally
            {
                pin.Free();
            }
            return string.IsNullOrEmpty(_capturedReply) ? null : _capturedReply;
        }
        finally
        {
            DestroyWindow(receiver);
        }
    }
    /// <summary>Gets the current folder path of XYplorer's active pane/tab.</summary>
    public static string? QueryCurrentPath(IntPtr xyHwnd) => QueryExpression(xyHwnd, "\"<curpath>\"")?.Trim();

    /// <summary>
    /// Gets every open tab path from both XYplorer panes through its script API.
    /// </summary>
    public static IReadOnlyList<string> QueryOpenTabPaths(IntPtr xyHwnd)
    {
        var paths = new List<string>();
        AddPaths(paths, QueryExpression(xyHwnd, "get(\"tabs\", <crlf>, \"a\")"));
        AddPaths(paths, QueryExpression(xyHwnd, "get(\"tabs\", <crlf>, \"i\")"));
        return paths;
    }

    private static void AddPaths(List<string> paths, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        foreach (var rawPath in value.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries))
        {
            var path = rawPath.Trim();
            if (!string.IsNullOrWhiteSpace(path))
                paths.Add(path);
        }
    }

    /// <summary>
    /// Navigates XYplorer's active pane to <paramref name="path"/> via the <c>goto</c> script command
    /// (fire-and-forget -- no reply is needed, so no receiver window is created). For a file path, XYplorer's
    /// own <c>goto</c> opens the containing folder and selects the item, matching Total Commander adapter's
    /// "place cursor on item" behavior without needing a separate flag.
    ///
    /// <paramref name="stealFocus"/> defaults to true (ExecuteItem's existing behavior: the user is
    /// switching to XYplorer, so taking foreground is correct). Live selection-mirroring
    /// (OnSelectionChanged) passes false instead -- unverified whether XYplorer's WM_COPYDATA handler
    /// actually requires being foreground the way Total Commander's does, but calling SetForegroundWindow
    /// on every arrow-key move while the user is still typing in Lertaro's own search box would steal
    /// keyboard focus away from it regardless, so it's skipped unless the caller actually wants to switch
    /// windows.
    /// </summary>
    public static bool Navigate(IntPtr xyHwnd, string path, bool stealFocus = true)
    {
        if (xyHwnd == IntPtr.Zero || string.IsNullOrEmpty(path)) return false;

        if (stealFocus) SetForegroundWindow(xyHwnd);
        var escaped = path.Replace("\"", "\"\"");
        return RunScript(xyHwnd, $"::goto \"{escaped}\";");
    }

    /// <summary>
    /// Selects <paramref name="path"/> (a full path, not a bare name) in whatever folder XYplorer's active
    /// pane already has open, via the <c>SelectItems</c> script command -- confirmed (XYplorer's own forum,
    /// "Open folders and select files in both panes from command line") as <c>selectitems 'FullFileName'</c>,
    /// single-quoted rather than the double-quoted style every other command here uses. Unlike <c>goto</c>,
    /// this never navigates, so it's the right verb for a directory result during live selection-mirroring
    /// (OnSelectionChanged): <c>goto</c> on a folder path enters it, which a live "just highlight it as the
    /// user arrows past" preview must not do. Never steals foreground (see OnSelectionChanged's own
    /// comment) -- there is no ExecuteItem-equivalent caller for this that would want a window switch.
    /// </summary>
    public static bool SelectItem(IntPtr xyHwnd, string path)
    {
        if (xyHwnd == IntPtr.Zero || string.IsNullOrEmpty(path)) return false;

        var escaped = path.Replace("'", "''");
        return RunScript(xyHwnd, $"::selectitems '{escaped}';");
    }

    private static bool RunScript(IntPtr xyHwnd, string script)
    {
        var bytes = Encoding.Unicode.GetBytes(script);
        var pin = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try
        {
            var cds = new COPYDATASTRUCT
            {
                dwData = (IntPtr)RUN_SCRIPT,
                cbData = bytes.Length,
                lpData = pin.AddrOfPinnedObject()
            };
            SendMessage(xyHwnd, WM_COPYDATA, IntPtr.Zero, ref cds);
        }
        finally
        {
            pin.Free();
        }
        return true;
    }
}
