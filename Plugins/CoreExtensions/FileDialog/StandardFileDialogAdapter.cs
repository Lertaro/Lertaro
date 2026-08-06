using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Lertaro.PluginSdk.Services;
using Lertaro.PluginSdk.Helpers;

using Lertaro.PluginSdk.Abstractions.Plugins.WindowAdapters;
namespace Lertaro.Plugins.CoreExtensions.FileDialog;

public class StandardFileDialogAdapter : IFileDialogAdapter
{
    public string Name => TranslationService.Get("Plugins_StandardFileDialogAdapterName");

    // Set by CanHandle for whichever hwnd it last matched -- read back by TargetIsFolderOnly below.
    // Safe as instance state (not per-hwnd) because this adapter, like every IFileDialogAdapter, is a
    // long-lived singleton tracking exactly one "currently active" dialog at a time (see
    // ExplorerTracker/InlineSearchNavigator), never two concurrently.
    private bool _lastMatchWasFolderOnly;

    public bool CanHandle(IntPtr hwnd, string className, string processName)
    {
        if (!className.Equals("#32770", StringComparison.OrdinalIgnoreCase))
            return false;
        if (FindBreadcrumbParent(hwnd) == IntPtr.Zero)
            return false;
        _lastMatchWasFolderOnly = LooksLikeFolderOnlyPicker(hwnd);
        return true;
    }

    // True for a modern dialog opened via FOS_PICKFOLDERS (a "Browse For Folder"-style picker built on
    // the same IFileOpenDialog frame as a regular Open/Save dialog, as opposed to the legacy
    // SHBrowseForFolder dialog FolderBrowserDialogAdapter already covers). Determined empirically by
    // comparing a real modern file picker against a real modern folder picker: folder mode swaps out
    // the filename ComboBoxEx32 (control id 1148) for a plain Edit box (control id 1152) in the same
    // slot. Checking for id 1152's PRESENCE together with id 1148's ABSENCE (not either alone) is
    // deliberately conservative -- a single missing control could just mean an unrelated customization,
    // e.g. Office's Open dialog bolts extra panels (Recent/OneDrive/SharePoint) onto this same shell
    // frame via IFileDialogCustomize, but can't remove or renumber the shell's own built-in id-1148
    // combo since that part isn't Office's to customize, only add alongside.
    public bool TargetIsFolderOnly => _lastMatchWasFolderOnly;

    private static bool LooksLikeFolderOnlyPicker(IntPtr hwnd)
    {
        var hasFileNameCombo = FindDescendant(hwnd, "ComboBoxEx32", 1148) != IntPtr.Zero;
        var hasFolderEdit = FindDescendant(hwnd, "Edit", 1152) != IntPtr.Zero;
        return hasFolderEdit && !hasFileNameCombo;
    }

    public string? GetCurrentPath(IntPtr hwnd)
    {
        try
        {
            if (hwnd == IntPtr.Zero) return null;
            var breadcrumbParent = FindBreadcrumbParent(hwnd);
            if (breadcrumbParent != IntPtr.Zero)
            {
                var child = FindWindowEx(breadcrumbParent, IntPtr.Zero, "ToolbarWindow32", null);
                while (child != IntPtr.Zero)
                {
                    var textSb = new StringBuilder(1024);
                    SendMessage(child, WM_GETTEXT, (IntPtr)textSb.Capacity, textSb);
                    var text = textSb.ToString().Trim();
                    var potentialPath = text;
                    var colonIndex = text.IndexOf(':');
                    if (colonIndex >= 0)
                    {
                        var isDriveLetter = colonIndex == 1 && text.Length >= 2 &&
                            ((text[0] >= 'a' && text[0] <= 'z') || (text[0] >= 'A' && text[0] <= 'Z'));
                        if (!isDriveLetter && colonIndex + 1 < text.Length)
                            potentialPath = text.Substring(colonIndex + 1).Trim();
                    }

                    if (!string.IsNullOrEmpty(potentialPath))
                    {
                        var resolved = ShellPathHelper.ResolveSpecialFolder(potentialPath);
                        var isValid = Directory.Exists(resolved) ||
                                      (resolved.Length >= 3 && resolved[1] == ':' && resolved[2] == '\\' && char.IsLetter(resolved[0]));

                        if (isValid) return resolved;
                    }
                    child = FindWindowEx(breadcrumbParent, child, "ToolbarWindow32", null);
                }
            }
        }
        catch { }
        return null;
    }

    public bool NavigateTo(IntPtr hwnd, string targetPath)
    {
        try
        {
            var targetEdit = FindSubEditBox(hwnd);
            if (targetEdit == IntPtr.Zero) return false;

            if (Directory.Exists(targetPath) && !targetPath.EndsWith("\\"))
                targetPath += "\\";

            var currentPath = GetCurrentPath(hwnd);
            if (currentPath != null && string.Equals(currentPath.TrimEnd('\\'), targetPath.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                return true;

            SendMessage(targetEdit, WM_SETTEXT, IntPtr.Zero, targetPath);
            var parent = GetParent(targetEdit);
            var ctrlId = GetDlgCtrlID(targetEdit);
            if (parent != IntPtr.Zero)
            {
                var wParamChange = (IntPtr)((EN_CHANGE << 16) | (uint)ctrlId);
                SendMessage(parent, WM_COMMAND, wParamChange, targetEdit);
            }

            Task.Run(async () =>
            {
                await Task.Delay(300);
                var currentActive = GetForegroundWindow();
                var isAllowed = (currentActive == hwnd);

                if (isAllowed)
                {
                    var targetThread = GetWindowThreadProcessId(targetEdit, out var _);
                    var currentThread = GetCurrentThreadId();
                    var attached = false;
                    try
                    {
                        if (targetThread != 0 && targetThread != currentThread)
                            attached = AttachThreadInput(currentThread, targetThread, true);

                        SetForegroundWindow(hwnd);
                        SetFocus(targetEdit);
                        PostMessage(targetEdit, WM_KEYDOWN, (IntPtr)VK_RETURN, IntPtr.Zero);
                        PostMessage(targetEdit, WM_KEYUP, (IntPtr)VK_RETURN, IntPtr.Zero);
                        PostMessage(targetEdit, WM_LBUTTONDOWN, (IntPtr)1, IntPtr.Zero);
                        PostMessage(targetEdit, WM_LBUTTONUP, IntPtr.Zero, IntPtr.Zero);
                        PostMessage(targetEdit, EM_SETSEL, IntPtr.Zero, (IntPtr)(-1));
                    }
                    finally
                    {
                        if (attached) AttachThreadInput(currentThread, targetThread, false);
                    }
                }
            });
            return true;
        }
        catch { return false; }
    }

    public bool GetDockBounds(IntPtr hwnd, out AdapterRect rect)
    {
        rect = default;
        if (hwnd == IntPtr.Zero) return false;
        var nativeRect = new RECT();
        var result = DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out nativeRect, Marshal.SizeOf<RECT>());
        if (result == 0)
        {
            rect = new AdapterRect { Left = nativeRect.Left, Top = nativeRect.Top, Right = nativeRect.Right, Bottom = nativeRect.Bottom };
            return true;
        }
        if (GetWindowRect(hwnd, out nativeRect))
        {
            rect = new AdapterRect { Left = nativeRect.Left, Top = nativeRect.Top, Right = nativeRect.Right, Bottom = nativeRect.Bottom };
            return true;
        }
        return false;
    }

    public bool RestoreFocus(IntPtr hwnd)
    {
        try
        {
            var targetEdit = FindSubEditBox(hwnd);
            if (targetEdit == IntPtr.Zero) return false;
            var targetThread = GetWindowThreadProcessId(targetEdit, out var _);
            var currentThread = GetCurrentThreadId();
            var attached = false;
            try
            {
                if (targetThread != 0 && targetThread != currentThread)
                    attached = AttachThreadInput(currentThread, targetThread, true);

                SetForegroundWindow(hwnd);
                SetFocus(targetEdit);
                PostMessage(targetEdit, EM_SETSEL, IntPtr.Zero, (IntPtr)(-1));
                return true;
            }
            finally
            {
                if (attached) AttachThreadInput(currentThread, targetThread, false);
            }
        }
        catch { return false; }
    }

    #region Win32 API Helpers
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string? lpszClass, string? lpszWindow);
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, StringBuilder lParam);
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, string lParam);
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")]
    private static extern IntPtr GetParent(IntPtr hWnd);
    [DllImport("user32.dll")]
    private static extern int GetDlgCtrlID(IntPtr hWnd);
    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")]
    private static extern IntPtr SetFocus(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    private const uint WM_GETTEXT = 0x000D;
    private const uint WM_SETTEXT = 0x000C;
    private const uint WM_COMMAND = 0x0111;
    private const uint EN_CHANGE = 0x0300;
    private const uint WM_KEYDOWN = 0x0100;
    private const uint WM_KEYUP = 0x0101;
    private const uint WM_LBUTTONDOWN = 0x0201;
    private const uint WM_LBUTTONUP = 0x0202;
    private const uint EM_SETSEL = 0x00B1;
    private const int VK_RETURN = 0x0D;
    private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    private delegate bool EnumChildProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(IntPtr hwndParent, EnumChildProc lpEnumFunc, IntPtr lParam);

    private static IntPtr FindBreadcrumbParent(IntPtr parent)
    {
        if (parent == IntPtr.Zero) return IntPtr.Zero;
        var result = IntPtr.Zero;
        EnumChildWindows(parent, (childHwnd, lParam) =>
        {
            var classNameSb = new StringBuilder(256);
            GetClassName(childHwnd, classNameSb, classNameSb.Capacity);
            if (classNameSb.ToString().Equals("Breadcrumb Parent", StringComparison.OrdinalIgnoreCase))
            {
                result = childHwnd;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        return result;
    }

    private static IntPtr FindDescendant(IntPtr parent, string className, int controlId)
    {
        if (parent == IntPtr.Zero) return IntPtr.Zero;
        var result = IntPtr.Zero;
        EnumChildWindows(parent, (childHwnd, lParam) =>
        {
            var classNameSb = new StringBuilder(256);
            GetClassName(childHwnd, classNameSb, classNameSb.Capacity);
            if (classNameSb.ToString().Equals(className, StringComparison.OrdinalIgnoreCase) && GetDlgCtrlID(childHwnd) == controlId)
            {
                result = childHwnd;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        return result;
    }

    private static IntPtr FindSubEditBox(IntPtr parent)
    {
        if (parent == IntPtr.Zero) return IntPtr.Zero;
        var result = IntPtr.Zero;
        EnumChildWindows(parent, (childHwnd, lParam) =>
        {
            var classNameSb = new StringBuilder(256);
            GetClassName(childHwnd, classNameSb, classNameSb.Capacity);
            if (classNameSb.ToString().Equals("Edit", StringComparison.OrdinalIgnoreCase))
            {
                result = childHwnd;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        return result;
    }
    #endregion
}
