using System.Runtime.InteropServices;
using System.Text;
using Lertaro.PluginSdk.Services;

using Lertaro.PluginSdk.Abstractions.Plugins.WindowAdapters;
namespace Lertaro.Plugins.CoreExtensions.FileDialog;

public class FolderBrowserDialogAdapter : IFileDialogAdapter
{
    public string Name => TranslationService.Get("Plugins_FolderBrowserDialogAdapterName");

    // SHBrowseForFolder only ever picks a folder -- there's no filename box for it to alternatively want a
    // specific file for, unlike ClassicFileDialogAdapter/StandardFileDialogAdapter's Open/Save dialogs. See
    // IFileDialogAdapter.TargetIsFolderOnly for why callers use this.
    public bool TargetIsFolderOnly => true;

    public bool CanHandle(IntPtr hwnd, string className, string processName)
    {
        if (!className.Equals("#32770", StringComparison.OrdinalIgnoreCase))
            return false;

        // Ignore standard file dialogs which have breadcrumbs
        if (FindBreadcrumbParent(hwnd) != IntPtr.Zero)
            return false;

        var treeView = FindTreeView(hwnd);
        if (treeView == IntPtr.Zero)
            return false;

        // Folder browser dialog (SHBrowseForFolder) TreeView has standard control ID 14145 (0x3741) or 100 (0x64) in modern styles
        var ctrlId = GetDlgCtrlID(treeView);
        return ctrlId == 14145 || ctrlId == 100;
    }

    public string? GetCurrentPath(IntPtr hwnd) =>
        // Getting the current selected path from external TreeView is complex/unreliable.
        // Returning null is safe and will still allow NavigateTo to work.
        null;

    public bool NavigateTo(IntPtr hwnd, string targetPath)
    {
        try
        {
            if (hwnd == IntPtr.Zero) return false;

            GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == 0) return false;

            var hProcess = OpenProcess(PROCESS_VM_OPERATION | PROCESS_VM_WRITE, false, (int)pid);
            if (hProcess == IntPtr.Zero) return false;

            try
            {
                var pathBytes = Encoding.Unicode.GetBytes(targetPath + "\0");
                var size = (uint)pathBytes.Length;

                var remoteMem = VirtualAllocEx(hProcess, IntPtr.Zero, size, MEM_COMMIT, PAGE_READWRITE);
                if (remoteMem == IntPtr.Zero) return false;

                try
                {
                    if (WriteProcessMemory(hProcess, remoteMem, pathBytes, size, out _))
                    {
                        SendMessage(hwnd, BFFM_SETSELECTIONW, (IntPtr)1, remoteMem);
                        return true;
                    }
                }
                finally
                {
                    VirtualFreeEx(hProcess, remoteMem, 0, MEM_RELEASE);
                }
            }
            finally
            {
                CloseHandle(hProcess);
            }
        }
        catch { }
        return false;
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
            var treeView = FindTreeView(hwnd);
            if (treeView != IntPtr.Zero)
            {
                SetForegroundWindow(hwnd);
                SetFocus(treeView);
                return true;
            }
        }
        catch { }
        return false;
    }

    #region Win32 API Helpers
    [DllImport("user32.dll")]
    private static extern int GetDlgCtrlID(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string? lpszClass, string? lpszWindow);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr SetFocus(IntPtr hWnd);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr lpAddress, uint dwSize, uint flAllocationType, uint flProtect);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, byte[] lpBuffer, uint nSize, out int lpNumberOfBytesWritten);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool VirtualFreeEx(IntPtr hProcess, IntPtr lpAddress, uint dwSize, uint dwFreeType);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    private const uint BFFM_SETSELECTIONW = 0x0400 + 103;
    private const uint PROCESS_VM_OPERATION = 0x0008;
    private const uint PROCESS_VM_WRITE = 0x0020;
    private const uint MEM_COMMIT = 0x1000;
    private const uint MEM_RELEASE = 0x8000;
    private const uint PAGE_READWRITE = 0x04;
    private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    private static IntPtr FindBreadcrumbParent(IntPtr parent)
    {
        var child = FindWindowEx(parent, IntPtr.Zero, null, null);
        while (child != IntPtr.Zero)
        {
            var classNameSb = new StringBuilder(256);
            GetClassName(child, classNameSb, classNameSb.Capacity);
            if (classNameSb.ToString().Equals("Breadcrumb Parent", StringComparison.OrdinalIgnoreCase))
                return child;
            var subParent = FindBreadcrumbParent(child);
            if (subParent != IntPtr.Zero) return subParent;
            child = FindWindowEx(parent, child, null, null);
        }
        return IntPtr.Zero;
    }

    private static IntPtr FindTreeView(IntPtr parent)
    {
        var tree = FindWindowEx(parent, IntPtr.Zero, "SysTreeView32", null);
        if (tree != IntPtr.Zero) return tree;

        var child = FindWindowEx(parent, IntPtr.Zero, null, null);
        while (child != IntPtr.Zero)
        {
            var subTree = FindTreeView(child);
            if (subTree != IntPtr.Zero) return subTree;
            child = FindWindowEx(parent, child, null, null);
        }
        return IntPtr.Zero;
    }
    #endregion
}
