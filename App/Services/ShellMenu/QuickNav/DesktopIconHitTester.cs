using System.Runtime.InteropServices;
using Native = Lertaro.Core.Hook.ExplorerNativeHooks;
using PointNative = Lertaro.App.Views.InlineSearchWindow.Helpers.InlineSearchWindowNativeMethods;

namespace Lertaro.App.Services.ShellMenu.QuickNav;

// Cross-process hit test to distinguish "clicked a desktop icon" from "clicked empty desktop space" --
// SysListView32 (the desktop's icon list) doesn't expose this any other way, so this allocates a small
// buffer in the remote process, asks it to fill in an LVM_HITTEST result, and reads it back. Kept
// separate from QuickNavigationTriggerGate's own gating policy: a bug fix here is about the remote-memory
// protocol, not about when the popup should show.
internal static class DesktopIconHitTester
{
    [StructLayout(LayoutKind.Sequential)]
    private struct LVHITTESTINFO
    {
        public PointNative.POINT pt;
        public uint flags;
        public int iItem;
        public int iSubItem;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ScreenToClient(IntPtr hWnd, ref PointNative.POINT lpPoint);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr lpAddress, uint dwSize, uint flAllocationType, uint flProtect);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, ref LVHITTESTINFO lpBuffer, uint nSize, out IntPtr lpNumberOfBytesWritten);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, out LVHITTESTINFO lpBuffer, uint nSize, out IntPtr lpNumberOfBytesRead);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool VirtualFreeEx(IntPtr hProcess, IntPtr lpAddress, uint dwSize, uint dwFreeType);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);

    // Minimal rights this hit-test protocol actually needs: allocate/write/read remote memory and
    // query the process -- not PROCESS_ALL_ACCESS, which is both unnecessary and can fail outright
    // under stricter integrity-level boundaries.
    private const uint PROCESS_VM_OPERATION = 0x0008;
    private const uint PROCESS_VM_READ = 0x0010;
    private const uint PROCESS_VM_WRITE = 0x0020;
    private const uint PROCESS_QUERY_INFORMATION = 0x0400;

    public static bool IsPointOnDesktopIcon(IntPtr hwndListView, int x, int y)
    {
        var hProcess = IntPtr.Zero;
        var pRemoteMem = IntPtr.Zero;
        try
        {
            Native.GetWindowThreadProcessId(hwndListView, out var pid);
            hProcess = OpenProcess(
                PROCESS_VM_OPERATION | PROCESS_VM_READ | PROCESS_VM_WRITE | PROCESS_QUERY_INFORMATION,
                false, pid);
            if (hProcess == IntPtr.Zero) return false;

            var pt = new PointNative.POINT { x = x, y = y };
            ScreenToClient(hwndListView, ref pt);

            var hitTestInfo = new LVHITTESTINFO
            {
                pt = pt,
                flags = 0,
                iItem = -1,
                iSubItem = -1
            };

            pRemoteMem = VirtualAllocEx(hProcess, IntPtr.Zero, (uint)Marshal.SizeOf<LVHITTESTINFO>(), 0x1000 /* MEM_COMMIT */, 0x04 /* PAGE_READWRITE */);
            if (pRemoteMem == IntPtr.Zero) return false;

            WriteProcessMemory(hProcess, pRemoteMem, ref hitTestInfo, (uint)Marshal.SizeOf<LVHITTESTINFO>(), out _);

            PointNative.SendMessage(hwndListView, 0x1012 /* LVM_HITTEST */, IntPtr.Zero, pRemoteMem);

            ReadProcessMemory(hProcess, pRemoteMem, out hitTestInfo, (uint)Marshal.SizeOf<LVHITTESTINFO>(), out _);

            return hitTestInfo.iItem != -1;
        }
        catch { }
        finally
        {
            if (pRemoteMem != IntPtr.Zero) VirtualFreeEx(hProcess, pRemoteMem, 0, 0x8000 /* MEM_RELEASE */);
            if (hProcess != IntPtr.Zero) CloseHandle(hProcess);
        }
        return false;
    }
}
