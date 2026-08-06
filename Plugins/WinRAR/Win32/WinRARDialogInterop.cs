using System.Runtime.InteropServices;
using System.Text;

namespace Lertaro.Plugins.WinRAR.Win32;

// Raw Win32 plumbing for WinRAR's "Extract path and options" dialog. Nothing here reads window titles --
// WinRAR is localized, so the dialog's caption text and every control's label vary by language/version,
// but the dialog's compiled resource control IDs (below) don't: a language pack only translates the
// string tables, never the numeric IDs baked into the dialog template itself. Confirmed by live-inspecting
// an actual open dialog (GetWindow/GetDlgCtrlID walk) rather than assumed from any documentation.
internal static class WinRARDialogInterop
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern int GetDlgCtrlID(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    // Plain GetWindowText/GetWindowTextLength turned out unreliable here: confirmed empirically that
    // reading the path ComboBox intermittently returns empty even though it visibly shows a real path,
    // on no fixed schedule -- classic symptom of a cross-process message reaching a target whose message
    // queue is momentarily busy. SendMessageTimeout with SMTO_ABORTIFHUNG is the established fix for this
    // exact class of flakiness elsewhere in this codebase (Core/Hook/InlineSearch/KeyboardUtils.cs,
    // WindowSwitcher's own WindowEnumerator.cs) -- CharSet.Unicode is required, not cosmetic: without it
    // this binds to SendMessageTimeoutA, which mangles non-ASCII path characters (mirrors the mojibake
    // bug already fixed once in WindowEnumerator.cs for the identical reason).
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, uint flags, uint timeoutMs, out IntPtr result);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, StringBuilder lParam, uint flags, uint timeoutMs, out IntPtr result);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

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

    private const uint GW_CHILD = 5;
    private const uint GW_HWNDNEXT = 2;
    private const uint EM_SETSEL = 0x00B1;
    private const uint BM_CLICK = 0x00F5;
    private const uint WM_SETTEXT = 0x000C;
    private const uint WM_GETTEXT = 0x000D;
    private const uint WM_GETTEXTLENGTH = 0x000E;
    private const uint SMTO_ABORTIFHUNG = 0x0002;
    private const uint GetTextTimeoutMs = 150;
    private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;

    // The destination-path ComboBox (its own internal Edit child is what actually receives keyboard
    // focus), the folder tree beside it, the "Display" button, and the tab control wrapping all of them --
    // required together as one combined fingerprint (see LooksLikeExtractDialog) so WinRAR's OTHER #32770
    // dialogs (password prompt, About, ...) never false-trigger just because one of these IDs coincidentally
    // matches.
    public const int ComboBoxId = 101;
    public const int EditId = 1001;
    public const int TreeViewId = 104;
    public const int TabControlId = 12320;

    // The "Display" button next to the path combo (its label is localized along with the rest of the dialog).
    // Setting the Edit's text alone doesn't make WinRAR's folder tree follow it -- confirmed empirically.
    // Taken directly from Listary's own shipped WinRAR plugin
    // (github.com/listary/Listary.FileAppPlugin.WinRAR, ExtractDialogTab.cs), which simulates a click
    // (BM_CLICK) on this button right after setting the text: it's WinRAR's own built-in "apply what's
    // typed" action, and reverse-engineering this dialog's exact behavior wasn't something to verify any
    // other way without WinRAR's own source.
    public const int DisplayButtonId = 102;

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
    // rather than EnumChildWindows -- the dialog's path ComboBox/TreeView sit inside a nested #32770 tab
    // page, not as direct children of the outer dialog, so a plain single-level enumeration would miss them.
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
        bool hasTab = false, hasCombo = false, hasEdit = false, hasTree = false, hasDisplayButton = false;
        foreach (var d in Descendants(hWnd))
        {
            var id = GetDlgCtrlID(d);
            var cls = GetClassNameOf(d);
            if (id == TabControlId && cls.Equals("SysTabControl32", StringComparison.OrdinalIgnoreCase)) hasTab = true;
            else if (id == ComboBoxId && cls.Equals("ComboBox", StringComparison.OrdinalIgnoreCase)) hasCombo = true;
            else if (id == EditId && cls.Equals("Edit", StringComparison.OrdinalIgnoreCase)) hasEdit = true;
            else if (id == TreeViewId && cls.Equals("SysTreeView32", StringComparison.OrdinalIgnoreCase)) hasTree = true;
            else if (id == DisplayButtonId && cls.Equals("Button", StringComparison.OrdinalIgnoreCase)) hasDisplayButton = true;

            if (hasTab && hasCombo && hasEdit && hasTree && hasDisplayButton) return true;
        }
        return false;
    }

    public static IntPtr FindPathEdit(IntPtr dialogHwnd) => FindById(dialogHwnd, EditId, "Edit");

    public static IntPtr FindPathCombo(IntPtr dialogHwnd) => FindById(dialogHwnd, ComboBoxId, "ComboBox");

    public static IntPtr FindDisplayButton(IntPtr dialogHwnd) => FindById(dialogHwnd, DisplayButtonId, "Button");

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

    // Uses SendMessage(WM_SETTEXT) directly rather than the SetWindowText API: same underlying message,
    // but this is the exact technique ClassicFileDialogAdapter already uses successfully for Explorer's own
    // classic dialog, and SetWindowText alone was observed to report success (and even read back correctly
    // right after) without the change ever appearing on screen in WinRAR's dialog.
    public static bool SetText(IntPtr hWnd, string text) => hWnd != IntPtr.Zero && SendMessage(hWnd, WM_SETTEXT, IntPtr.Zero, text) != IntPtr.Zero;

    // Simulates pressing the Display button -- see DisplayButtonId's own comment for why this, and not any
    // keystroke simulation, is what actually makes WinRAR treat a programmatically-set path as "entered".
    public static void ClickDisplayButton(IntPtr dialogHwnd) => PostMessage(FindDisplayButton(dialogHwnd), BM_CLICK, IntPtr.Zero, IntPtr.Zero);

    // DWM's extended frame bounds (excludes the drop shadow) first, falling back to the plain window
    // rect -- same two-step ClassicFileDialogAdapter/FolderBrowserDialogAdapter already use for docking
    // over Explorer's own common dialogs.
    public static bool TryGetDialogRect(IntPtr dialogHwnd, out RECT rect)
    {
        if (DwmGetWindowAttribute(dialogHwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out rect, Marshal.SizeOf<RECT>()) == 0)
            return true;
        return GetWindowRect(dialogHwnd, out rect);
    }

    // Cross-thread/cross-process focus needs the AttachThreadInput dance -- SetFocus alone silently no-ops
    // when the calling thread isn't already attached to the target window's input queue. Same shape
    // ClassicFileDialogAdapter.RestoreFocus already uses for Explorer's own common dialog. Also selects the
    // full text (EM_SETSEL) so the user can immediately overtype instead of having to clear it first.
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
