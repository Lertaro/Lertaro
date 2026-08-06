using System.Runtime.InteropServices;
using System.Text;

namespace Lertaro.Plugins.Bandizip.Win32;

// Raw Win32 plumbing for Bandizip's "选择解压路径" (choose extract path) dialog. Nothing here reads window
// titles -- Bandizip is localized, so caption/control label text varies by language, but the dialog's
// compiled resource control IDs (below) don't. Confirmed by live-inspecting an actual open dialog
// (GetWindow/GetDlgCtrlID walk plus direct SendMessage experiments) rather than assumed from documentation.
internal static class BandizipDialogInterop
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern int GetDlgCtrlID(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern IntPtr GetParent(IntPtr hWnd);

    // SendMessageTimeout (not plain SendMessageTimeout-less GetWindowText) for the same reason as
    // WindowSwitcher's WindowEnumerator.cs and WinRAR's own WinRARDialogInterop.cs: a cross-process
    // GetWindowText/GetWindowTextLength can intermittently return empty when the target's message queue is
    // momentarily busy, with no exception or hang to signal it. CharSet.Unicode is required, not cosmetic --
    // without it this binds to the ANSI entry point and mangles non-ASCII path characters.
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, uint flags, uint timeoutMs, out IntPtr result);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, StringBuilder lParam, uint flags, uint timeoutMs, out IntPtr result);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr SetFocus(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, string lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    private const uint GW_CHILD = 5;
    private const uint GW_HWNDNEXT = 2;
    private const uint EM_SETSEL = 0x00B1;
    private const uint WM_SETTEXT = 0x000C;
    private const uint WM_GETTEXT = 0x000D;
    private const uint WM_GETTEXTLENGTH = 0x000E;
    private const uint WM_COMMAND = 0x0111;
    private const uint CBN_EDITCHANGE = 0x0005;
    private const uint SMTO_ABORTIFHUNG = 0x0002;
    private const uint GetTextTimeoutMs = 150;
    private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
    private const int SW_HIDE = 0;

    // Empirically, setting the Edit's text alone updates the display but the folder tree beside it doesn't
    // follow (confirmed live) -- Bandizip only reacts to the same CBN_EDITCHANGE notification a real
    // keystroke would generate, not to WM_SETTEXT by itself. Simulating that notification is what
    // Windows Shell's autocomplete engine (attached to this same Edit for path history) ALSO reacts to,
    // which pops up its own "AutoCompleteWindow" suggestion box as a side effect -- SuppressAutoComplete
    // exists specifically to close that unwanted popup back down.
    public const int ComboBoxId = 1021;
    public const int EditId = 1001;
    public const int TreeViewId = 1024;

    // Bandizip's "选择" (choose files to add to archive) dialog -- a completely different dialog template
    // from the extract-path one above (no ComboBox at all: the path field is a plain Edit, confirmed live),
    // but reuses the SAME tree-view control ID (1024) as the extract dialog. ListViewId/OpenButtonId round
    // out the fingerprint so this never false-matches some other #32770 Bandizip happens to show (About,
    // password prompt, ...).
    public const int AddFilesPathEditId = 1339;
    public const int AddFilesListViewId = 1094;
    public const int AddFilesOpenButtonId = 1293;

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    private static string GetClassNameOf(IntPtr hWnd)
    {
        var sb = new StringBuilder(64);
        GetClassName(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }

    // Walks every descendant (children, and their children) of hWnd via GetWindow(GW_CHILD)/(GW_HWNDNEXT)
    // rather than EnumChildWindows -- matches the approach already proven reliable for WinRARDialogInterop.
    private static IEnumerable<IntPtr> Descendants(IntPtr hWnd)
    {
        var child = GetWindow(hWnd, GW_CHILD);
        while (child != IntPtr.Zero)
        {
            yield return child;
            foreach (var grandchild in Descendants(child))
                yield return grandchild;
            child = GetWindow(child, GW_HWNDNEXT);
        }
    }

    public static bool LooksLikeExtractDialog(IntPtr hWnd)
    {
        bool hasCombo = false, hasEdit = false, hasTree = false;
        foreach (var d in Descendants(hWnd))
        {
            var id = GetDlgCtrlID(d);
            var cls = GetClassNameOf(d);
            if (id == ComboBoxId && cls.Equals("ComboBox", StringComparison.OrdinalIgnoreCase)) hasCombo = true;
            else if (id == EditId && cls.Equals("Edit", StringComparison.OrdinalIgnoreCase)) hasEdit = true;
            else if (id == TreeViewId && cls.Equals("SysTreeView32", StringComparison.OrdinalIgnoreCase)) hasTree = true;

            if (hasCombo && hasEdit && hasTree) return true;
        }
        return false;
    }

    public static bool LooksLikeAddFilesDialog(IntPtr hWnd)
    {
        bool hasEdit = false, hasTree = false, hasList = false, hasOpenButton = false;
        foreach (var d in Descendants(hWnd))
        {
            var id = GetDlgCtrlID(d);
            var cls = GetClassNameOf(d);
            if (id == AddFilesPathEditId && cls.Equals("Edit", StringComparison.OrdinalIgnoreCase)) hasEdit = true;
            else if (id == TreeViewId && cls.Equals("SysTreeView32", StringComparison.OrdinalIgnoreCase)) hasTree = true;
            else if (id == AddFilesListViewId && cls.Equals("SysListView32", StringComparison.OrdinalIgnoreCase)) hasList = true;
            else if (id == AddFilesOpenButtonId && cls.Equals("Button", StringComparison.OrdinalIgnoreCase)) hasOpenButton = true;

            if (hasEdit && hasTree && hasList && hasOpenButton) return true;
        }
        return false;
    }

    public static IntPtr FindPathEdit(IntPtr dialogHwnd) => FindById(dialogHwnd, EditId, "Edit");

    public static IntPtr FindPathCombo(IntPtr dialogHwnd) => FindById(dialogHwnd, ComboBoxId, "ComboBox");

    public static IntPtr FindAddFilesPathEdit(IntPtr dialogHwnd) => FindById(dialogHwnd, AddFilesPathEditId, "Edit");

    private static IntPtr FindById(IntPtr dialogHwnd, int id, string expectedClass)
    {
        foreach (var d in Descendants(dialogHwnd))
        {
            if (GetDlgCtrlID(d) == id && GetClassNameOf(d).Equals(expectedClass, StringComparison.OrdinalIgnoreCase))
                return d;
        }
        return IntPtr.Zero;
    }

    public static string GetText(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero) return string.Empty;

        if (SendMessageTimeout(hWnd, WM_GETTEXTLENGTH, IntPtr.Zero, IntPtr.Zero, SMTO_ABORTIFHUNG, GetTextTimeoutMs, out var lengthResult) == IntPtr.Zero)
            return string.Empty;

        var len = lengthResult.ToInt32();
        if (len <= 0) return string.Empty;

        var sb = new StringBuilder(len + 1);
        if (SendMessageTimeout(hWnd, WM_GETTEXT, new IntPtr(sb.Capacity), sb, SMTO_ABORTIFHUNG, GetTextTimeoutMs, out _) == IntPtr.Zero)
            return string.Empty;

        return sb.ToString();
    }

    public static bool SetText(IntPtr hWnd, string text) => hWnd != IntPtr.Zero && SendMessage(hWnd, WM_SETTEXT, IntPtr.Zero, text) != IntPtr.Zero;

    // The combo's parent (not necessarily the outer dialog itself, though it is one and the same for this
    // particular dialog's flat layout) is what actually needs to see the notification -- WM_COMMAND
    // notifications always target the sending control's immediate parent, per standard Win32 dialog
    // routing, so this asks GetParent instead of assuming the caller's own hwnd is always correct.
    public static void NotifyEditChanged(IntPtr comboHwnd)
    {
        var parent = GetParent(comboHwnd);
        if (parent == IntPtr.Zero) return;
        var wParam = new IntPtr(ComboBoxId | (int)(CBN_EDITCHANGE << 16));
        SendMessage(parent, WM_COMMAND, wParam, comboHwnd);
    }

    // Simulating a real text edit (see NotifyEditChanged) also wakes up Windows' own shell autocomplete
    // engine, which is separately attached to this Edit for path history -- it shows an "AutoCompleteWindow"
    // (plus a "SysShadow" drop-shadow window) as its own top-level popup, confirmed live. Neither is part of
    // the dialog's own window tree (autocomplete popups are always separate top-level windows, never
    // children), so they have to be found by scanning the dialog's OWNING PROCESS's other top-level windows
    // rather than by descending from dialogHwnd, and hidden rather than left to flash on screen for
    // something the user never actually typed.
    public static void SuppressAutoComplete(IntPtr dialogHwnd)
    {
        GetWindowThreadProcessId(dialogHwnd, out var processId);
        if (processId == 0) return;

        EnumWindows((hWnd, _) =>
        {
            GetWindowThreadProcessId(hWnd, out var candidateProcessId);
            if (candidateProcessId == processId && IsWindowVisible(hWnd))
            {
                var cls = GetClassNameOf(hWnd);
                if (cls.Equals("AutoCompleteWindow", StringComparison.OrdinalIgnoreCase) || cls.Equals("SysShadow", StringComparison.OrdinalIgnoreCase))
                    ShowWindow(hWnd, SW_HIDE);
            }
            return true;
        }, IntPtr.Zero);
    }

    // DWM's extended frame bounds (excludes the drop shadow) first, falling back to the plain window
    // rect -- same two-step ClassicFileDialogAdapter/WinRARDialogInterop already use for docking over
    // other apps' common dialogs.
    public static bool TryGetDialogRect(IntPtr dialogHwnd, out RECT rect)
    {
        if (DwmGetWindowAttribute(dialogHwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out rect, Marshal.SizeOf<RECT>()) == 0)
            return true;
        return GetWindowRect(dialogHwnd, out rect);
    }

    // Cross-thread/cross-process focus needs the AttachThreadInput dance -- SetFocus alone silently no-ops
    // when the calling thread isn't already attached to the target window's input queue. Same shape
    // ClassicFileDialogAdapter.RestoreFocus/WinRARDialogInterop.SetForegroundAndFocus already use. Also
    // selects the full text (EM_SETSEL) so the user can immediately overtype instead of clearing it first.
    public static bool SetForegroundAndFocus(IntPtr dialogHwnd, IntPtr controlHwnd)
    {
        var targetThread = GetWindowThreadProcessId(controlHwnd, out _);
        var currentThread = GetCurrentThreadId();
        var attached = false;
        try
        {
            if (targetThread != 0 && targetThread != currentThread)
                attached = AttachThreadInput(currentThread, targetThread, true);

            SetForegroundWindow(dialogHwnd);
            SetFocus(controlHwnd);
            SendMessage(controlHwnd, EM_SETSEL, IntPtr.Zero, (IntPtr)(-1));
            return true;
        }
        finally
        {
            if (attached) AttachThreadInput(currentThread, targetThread, false);
        }
    }
}
