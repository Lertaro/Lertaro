using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Lertaro.App.Services.Notifications;

internal static class NotificationSessionState
{
    [DllImport("wtsapi32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSQuerySessionInformationW(
        IntPtr server, uint sessionId, int infoClass, out IntPtr buffer, out uint bytes);

    [DllImport("wtsapi32.dll", ExactSpelling = true)]
    private static extern void WTSFreeMemory(IntPtr buffer);

    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern DesktopHandle OpenInputDesktop(
        uint flags, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint access);

    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseDesktop(IntPtr desktop);

    private sealed class DesktopHandle() : SafeHandleZeroOrMinusOneIsInvalid(true)
    {
        protected override bool ReleaseHandle() => CloseDesktop(handle);
    }

    internal static bool ReadLocked()
    {
        const int sessionInfoEx = 25;
        var success = WTSQuerySessionInformationW(IntPtr.Zero, uint.MaxValue, sessionInfoEx, out var buffer, out var bytes);
        try
        {
            if (success && Parse(buffer, bytes) is { } locked) return locked;
        }
        finally
        {
            if (buffer != IntPtr.Zero) WTSFreeMemory(buffer);
        }

        // WTS may be unavailable when Remote Desktop Services is disabled. A normal user process cannot open
        // the secure input desktop. Hold reminders there too; the service rechecks a paused session periodically.
        const uint desktopReadObjects = 0x0001;
        using var desktop = OpenInputDesktop(0, false, desktopReadObjects);
        return desktop.IsInvalid;
    }

    internal static bool? Parse(IntPtr buffer, uint bytes)
    {
        // WTSINFOEXW: DWORD Level, then the 8-byte-aligned union. Its LEVEL1 starts with ULONG SessionId,
        // WTS_CONNECTSTATE_CLASS and LONG SessionFlags. Read only this prefix, not the names/timestamps after it.
        if (buffer == IntPtr.Zero || bytes < 20 || Marshal.ReadInt32(buffer) != 1) return null;
        return Marshal.ReadInt32(buffer, 16) switch
        {
            0 => true,
            1 => false,
            _ => null
        };
    }
}
