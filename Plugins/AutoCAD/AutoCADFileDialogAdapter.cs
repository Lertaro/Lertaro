using System.Runtime.InteropServices;
using System.Text;
using Lertaro.PluginSdk.Abstractions.Plugins.WindowAdapters;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.AutoCAD;

/// <summary>
/// Adapts AutoCAD's <c>Autodesk.AutoCAD.Windows.OpenFileDialog</c>.
/// </summary>
/// <remarks>
/// AutoCAD uses the native <c>#32770</c> dialog class, but its dialog template is not always identical
/// to the templates used by the generic adapters. The actual navigation and path messages are still the
/// common-dialog ones, so this adapter only supplies the narrower AutoCAD identity while keeping the
/// same cross-process message protocol as the classic implementation.
/// </remarks>
public sealed class AutoCADFileDialogAdapter : IFileDialogAdapter
{
    public string Name => TranslationService.Get("AutoCAD_FileDialogAdapterName");

    public bool TargetIsFolderOnly => false;

    public bool CanHandle(IntPtr hwnd, string className, string processName)
    {
        if (!AutoCADDialogIdentity.IsAutoCADProcess(processName)
            || !AutoCADDialogIdentity.IsCommonDialog(className)
            || hwnd == IntPtr.Zero)
            return false;

        return AutoCADDialogControls.LooksLikeFileDialog(hwnd);
    }

    public string? GetCurrentPath(IntPtr hwnd)
    {
        const int maxChars = 1024;
        var process = IntPtr.Zero;
        var remoteBuffer = IntPtr.Zero;
        try
        {
            GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == 0) return null;
            process = OpenProcess(PROCESS_VM_OPERATION | PROCESS_VM_READ | PROCESS_VM_WRITE, false, (int)pid);
            if (process == IntPtr.Zero) return null;
            remoteBuffer = VirtualAllocEx(process, IntPtr.Zero, (uint)(maxChars * sizeof(char)), MEM_COMMIT, PAGE_READWRITE);
            if (remoteBuffer == IntPtr.Zero) return null;
            // SendMessageTimeout, not SendMessage, so a hung dialog can't park the polling thread -- full
            // reason at Plugins/CoreExtensions/FileDialog/ClassicFileDialogAdapter.cs.
            if (SendMessageTimeout(hwnd, CDM_GETFOLDERPATH, (IntPtr)maxChars, remoteBuffer, SMTO_ABORTIFHUNG, GetPathTimeoutMs, out var result) == IntPtr.Zero) return null;
            if (result.ToInt64() <= 0) return null;
            var bytes = new byte[maxChars * sizeof(char)];
            if (!ReadProcessMemory(process, remoteBuffer, bytes, (uint)bytes.Length, out _)) return null;
            var path = Encoding.Unicode.GetString(bytes).TrimEnd('\0');
            return string.IsNullOrEmpty(path) ? null : path;
        }
        catch { return null; }
        finally
        {
            if (remoteBuffer != IntPtr.Zero) VirtualFreeEx(process, remoteBuffer, 0, MEM_RELEASE);
            if (process != IntPtr.Zero) CloseHandle(process);
        }
    }

    public bool NavigateTo(IntPtr hwnd, string targetPath)
    {
        try
        {
            var edit = FindFileNameEdit(hwnd);
            if (edit == IntPtr.Zero) return false;

            // Going into a folder is a step, not an answer: AutoCAD does navigate on a path in this box plus
            // Enter, then leaves that path sitting where the name belongs, so the user has to clear it by
            // hand and cannot get the default name back. A file's path IS the answer -- nothing to undo.
            var navigatingToFolder = Directory.Exists(targetPath);
            var remembered = navigatingToFolder ? ReadTextBox(edit) : string.Empty;

            if (navigatingToFolder && !targetPath.EndsWith("\\")) targetPath += "\\";
            SendMessage(edit, WM_SETTEXT, IntPtr.Zero, targetPath);
            var parent = GetParent(edit);
            var id = GetDlgCtrlID(edit);
            if (parent != IntPtr.Zero)
                SendMessage(parent, WM_COMMAND, (IntPtr)((EN_CHANGE << 16) | (uint)id), edit);

            Task.Run(async () =>
            {
                await Task.Delay(300);
                if (GetForegroundWindow() != hwnd) return;
                var targetThread = GetWindowThreadProcessId(edit, out _);
                var currentThread = GetCurrentThreadId();
                var attached = targetThread != 0 && targetThread != currentThread && AttachThreadInput(currentThread, targetThread, true);
                try
                {
                    SetForegroundWindow(hwnd);
                    SetFocus(edit);
                    PostMessage(edit, WM_KEYDOWN, (IntPtr)VK_RETURN, IntPtr.Zero);
                    PostMessage(edit, WM_KEYUP, (IntPtr)VK_RETURN, IntPtr.Zero);
                    PostMessage(edit, EM_SETSEL, IntPtr.Zero, (IntPtr)(-1));
                }
                finally
                {
                    if (attached) AttachThreadInput(currentThread, targetThread, false);
                }
            });

            if (navigatingToFolder)
            {
                var pathAskedFor = targetPath;
                Task.Run(async () =>
                {
                    // Past the commit above. Timed rather than watched for a signal because a dialog that
                    // never moved has nothing to report, and this runs off the UI thread either way.
                    await Task.Delay(1200);
                    var box = FindFileNameEdit(hwnd);
                    if (ShouldPutNameBack(boxNow: ReadTextBox(box), wrote: pathAskedFor))
                        SendMessage(box, WM_SETTEXT, IntPtr.Zero, remembered);
                });
            }

            return true;
        }
        catch { return false; }
    }

    /// <summary>
    /// Whether the remembered name may go back into the box after a folder navigation.
    /// </summary>
    /// <remarks>
    /// Only while the box still holds exactly the path this method wrote. Anything else in it -- the dialog
    /// having moved the field on after the navigation, or the user typing a name in the meantime -- is not
    /// ours to overwrite, and writing the remembered name back there would be the same damage this exists to
    /// undo. Called only for folder targets, so a picked file keeps its name in place.
    /// </remarks>
    internal static bool ShouldPutNameBack(string boxNow, string wrote) => boxNow == wrote;

    // WM_GETTEXT is one of the messages user32 marshals across the boundary itself, so the buffer stays in
    // this process. A path longer than the buffer reads back short, and the comparison above then refuses
    // to write anything -- the safe direction for a mistake.
    private static string ReadTextBox(IntPtr edit)
    {
        if (edit == IntPtr.Zero) return string.Empty;
        var buffer = new StringBuilder(1024);
        SendMessage(edit, WM_GETTEXT, (IntPtr)buffer.Capacity, buffer);
        return buffer.ToString();
    }

    public bool GetDockBounds(IntPtr hwnd, out AdapterRect rect)
    {
        rect = default;
        if (hwnd == IntPtr.Zero) return false;
        var native = new RECT();
        if (DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out native, Marshal.SizeOf<RECT>()) == 0
            || GetWindowRect(hwnd, out native))
        {
            rect = new AdapterRect { Left = native.Left, Top = native.Top, Right = native.Right, Bottom = native.Bottom };
            return true;
        }
        return false;
    }

    /// <summary>
    /// The dialog's file list -- the shell view AutoCAD hosts inside its own template -- which is what the
    /// card hangs from when there is no room under the dialog.
    /// </summary>
    /// <remarks>
    /// Without this answer the card falls back to the dialog's own top-right corner, which for a dialog this
    /// size sits in the caption and covers 关闭. Plain Win32 on purpose: the list is a real child window with
    /// a real rectangle, and this is asked again every time the dialog moves, so reaching into AutoCAD with
    /// UI Automation for it would be a cost paid on every one of those passes.
    /// </remarks>
    public bool TryGetFileListBounds(IntPtr hwnd, out AdapterRect bounds) =>
        TryGetDescendantBounds(hwnd, out bounds, ("SysListView32", 1), ("SHELLDLL_DefView", 0));

    /// <summary>
    /// The 文件名 row, used only when no file list could be found -- the rung under the list in the
    /// positioner's own order. Reported because AutoCAD's Open dialog is a template this process has not
    /// measured, and a card over a dialog whose list it cannot see should still line up with the row the user
    /// is feeding rather than with the caption.
    /// </summary>
    public bool TryGetTargetFieldBounds(IntPtr hwnd, out AdapterRect bounds) =>
        TryGetDescendantBounds(hwnd, out bounds, ("Edit", 1001));

    private static bool TryGetDescendantBounds(IntPtr hwnd, out AdapterRect bounds, params (string ClassName, int ControlId)[] candidates)
    {
        // Guarded because EnumChildWindows takes NULL as "the desktop", which would answer with some other
        // window's list on a dialog that has already gone.
        if (hwnd == IntPtr.Zero)
        {
            bounds = default;
            return false;
        }

        foreach (var (className, controlId) in candidates)
        {
            var child = FindDescendant(hwnd, className, controlId);
            if (child != IntPtr.Zero && GetWindowRect(child, out var rect) && rect.Right > rect.Left && rect.Bottom > rect.Top)
            {
                bounds = new AdapterRect { Left = rect.Left, Top = rect.Top, Right = rect.Right, Bottom = rect.Bottom };
                return true;
            }
        }

        bounds = default;
        return false;
    }

    public bool RestoreFocus(IntPtr hwnd)
    {
        try
        {
            var edit = FindFileNameEdit(hwnd);
            if (edit == IntPtr.Zero) return false;
            var targetThread = GetWindowThreadProcessId(edit, out _);
            var currentThread = GetCurrentThreadId();
            var attached = targetThread != 0 && targetThread != currentThread && AttachThreadInput(currentThread, targetThread, true);
            try
            {
                SetForegroundWindow(hwnd);
                SetFocus(edit);
                PostMessage(edit, EM_SETSEL, IntPtr.Zero, (IntPtr)(-1));
                return true;
            }
            finally
            {
                if (attached) AttachThreadInput(currentThread, targetThread, false);
            }
        }
        catch { return false; }
    }

    private static IntPtr FindFileNameEdit(IntPtr parent)
    {
        var standard = FindDescendant(parent, "Edit", 1152);
        return standard != IntPtr.Zero ? standard : FindDescendant(parent, "Edit", 1148) != IntPtr.Zero
            ? FindDescendant(parent, "Edit", 1148) : FindDescendant(parent, "Edit", 0);
    }

    private static IntPtr FindDescendant(IntPtr parent, string className, int controlId)
    {
        var result = IntPtr.Zero;
        EnumChildWindows(parent, (child, _) =>
        {
            var buffer = new StringBuilder(64);
            GetClassNameNative(child, buffer, buffer.Capacity);
            if (buffer.ToString().Equals(className, StringComparison.OrdinalIgnoreCase)
                && (controlId == 0 || GetDlgCtrlID(child) == controlId))
            {
                result = child;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        return result;
    }

    private delegate bool EnumChildProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(IntPtr hwndParent, EnumChildProc callback, IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "GetClassName", CharSet = CharSet.Unicode)]
    private static extern int GetClassNameNative(IntPtr hwnd, StringBuilder className, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hwnd, uint message, IntPtr wParam, string lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hwnd, uint message, IntPtr wParam, StringBuilder lParam);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam, uint fuFlags, uint uTimeout, out IntPtr lpdwResult);
    private const uint SMTO_ABORTIFHUNG = 0x0002;
    private const uint GetPathTimeoutMs = 500;

    [DllImport("user32.dll")]
    private static extern IntPtr GetParent(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern int GetDlgCtrlID(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out RECT value, int size);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint attach, uint attachTo, bool attachState);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern IntPtr SetFocus(IntPtr hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr VirtualAllocEx(IntPtr process, IntPtr address, uint size, uint allocationType, uint protect);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool VirtualFreeEx(IntPtr process, IntPtr address, uint size, uint freeType);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadProcessMemory(IntPtr process, IntPtr address, byte[] buffer, uint size, out IntPtr read);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    private const uint PROCESS_VM_OPERATION = 0x0008;
    private const uint PROCESS_VM_READ = 0x0010;
    private const uint PROCESS_VM_WRITE = 0x0020;
    private const uint MEM_COMMIT = 0x1000;
    private const uint MEM_RELEASE = 0x8000;
    private const uint PAGE_READWRITE = 0x04;
    private const uint CDM_GETFOLDERPATH = 0x0466;
    private const uint WM_SETTEXT = 0x000C;
    private const uint WM_GETTEXT = 0x000D;
    private const uint WM_COMMAND = 0x0111;
    private const uint EN_CHANGE = 0x0300;
    private const uint WM_KEYDOWN = 0x0100;
    private const uint WM_KEYUP = 0x0101;
    private const uint EM_SETSEL = 0x00B1;
    private const int VK_RETURN = 0x0D;
    private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}

internal static class AutoCADDialogControls
{
    public static bool LooksLikeFileDialog(IntPtr hwnd)
    {
        var hasFileNameEdit = false;
        var hasFolderView = false;
        var hasLocationCombo = false;

        EnumChildWindows(hwnd, (child, _) =>
        {
            var className = GetClassName(child);
            var controlId = GetDlgCtrlID(child);
            if (className.Equals("Edit", StringComparison.OrdinalIgnoreCase) && controlId == 1001)
                hasFileNameEdit = true;
            else if (className.Equals("SysListView32", StringComparison.OrdinalIgnoreCase) && controlId == 1)
                hasFolderView = true;
            else if (className.Equals("ComboBox", StringComparison.OrdinalIgnoreCase) && controlId == 1008)
                hasLocationCombo = true;

            return !(hasFileNameEdit && hasFolderView && hasLocationCombo);
        }, IntPtr.Zero);

        return hasFileNameEdit && hasFolderView && hasLocationCombo;
    }

    private static string GetClassName(IntPtr hwnd)
    {
        var buffer = new StringBuilder(256);
        return GetClassName(hwnd, buffer, buffer.Capacity) > 0 ? buffer.ToString() : string.Empty;
    }

    private delegate bool EnumChildProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(IntPtr hwndParent, EnumChildProc callback, IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "GetClassName", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hwnd, StringBuilder className, int maxCount);

    [DllImport("user32.dll")]
    private static extern int GetDlgCtrlID(IntPtr hwnd);
}
