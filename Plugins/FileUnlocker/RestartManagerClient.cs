using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace Lertaro.Plugins.FileUnlocker;

internal static class RestartManagerClient
{
    private const int ErrorMoreData = 234;
    // RmStartSession's session-key buffer must be at least CCH_RM_SESSION_KEY + 1 characters (the
    // key plus its NUL terminator, restartmanager.h) -- 32 alone violates the documented contract
    // and risks a 2-byte native overrun past the StringBuilder's buffer.
    private const int SessionKeyCapacity = 32 + 1;

    internal static RestartManagerResult Query(string path)
    {
        var startResult = StartSession(out var session, out var key);
        if (startResult != 0) return Failure(startResult);

        try
        {
            var registerResult = Register(session, path);
            if (registerResult != 0) return Failure(registerResult);
            return ReadProcesses(session);
        }
        finally
        {
            RmEndSession(session);
        }
    }

    internal static RestartManagerResult RequestShutdown(string path)
    {
        var startResult = StartSession(out var session, out var key);
        if (startResult != 0) return Failure(startResult);

        var shutdownResult = 0;
        try
        {
            var registerResult = Register(session, path);
            if (registerResult != 0) return Failure(registerResult);

            shutdownResult = RmShutdown(session, 0, IntPtr.Zero);
        }
        finally
        {
            RmEndSession(session);
        }

        // The old session retains the pre-shutdown process snapshot. Query a new session so the UI
        // reflects the file's current occupants rather than stale entries left by RmShutdown.
        return shutdownResult == 0 ? Query(path) : Failure(shutdownResult);
    }

    private static int StartSession(out uint session, out StringBuilder key)
    {
        key = new StringBuilder(SessionKeyCapacity);
        try
        {
            return RmStartSession(out session, 0, key);
        }
        catch (DllNotFoundException)
        {
            session = 0;
            return unchecked((int)0x8007007E);
        }
        catch (EntryPointNotFoundException)
        {
            session = 0;
            return unchecked((int)0x8007007F);
        }
    }

    private static int Register(uint session, string path) =>
        RmRegisterResources(session, 1, [path], 0, null, 0, null);

    private static RestartManagerResult ReadProcesses(uint session)
    {
        uint count = 0;
        var reason = RM_REBOOT_REASON.None;
        var result = RmGetList(session, out var needed, ref count, null, out reason);
        if (result == 0 || needed == 0) return RestartManagerResult.Success([]);
        if (result != ErrorMoreData) return Failure(result);

        var processes = new RM_PROCESS_INFO[needed];
        count = needed;
        result = RmGetList(session, out needed, ref count, processes, out reason);
        if (result != 0) return Failure(result);

        var items = new List<LockedProcess>((int)count);
        var seen = new HashSet<int>();
        for (var index = 0; index < count; index++)
        {
            var info = processes[index];
            var pid = info.Process.dwProcessId;
            if (pid <= 0 || !seen.Add(pid)) continue;
            items.Add(CreateProcess(info));
        }

        return RestartManagerResult.Success(items);
    }

    private static LockedProcess CreateProcess(RM_PROCESS_INFO info)
    {
        var name = info.strAppName;
        var path = string.Empty;
        try
        {
            using var process = Process.GetProcessById(info.Process.dwProcessId);
            name = process.ProcessName + ".exe";
            path = process.MainModule?.FileName ?? string.Empty;
        }
        catch
        {
            // Restart Manager can still identify a process after it exits or when its details require elevation.
        }

        return new LockedProcess(
            string.IsNullOrWhiteSpace(name) ? "Unknown" : name,
            info.Process.dwProcessId,
            path,
            info.ApplicationType.ToString());
    }

    private static RestartManagerResult Failure(int errorCode) =>
        RestartManagerResult.Failed(new Win32Exception(errorCode).Message);

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmStartSession(out uint sessionHandle, int sessionFlags, StringBuilder sessionKey);

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmRegisterResources(
        uint sessionHandle,
        uint fileCount,
        string[]? fileNames,
        uint applicationCount,
        RM_UNIQUE_PROCESS[]? applications,
        uint serviceCount,
        string[]? serviceNames);

    [DllImport("rstrtmgr.dll")]
    private static extern int RmGetList(
        uint sessionHandle,
        out uint processInfoNeeded,
        ref uint processInfoCount,
        [In, Out] RM_PROCESS_INFO[]? processInfo,
        out RM_REBOOT_REASON rebootReasons);

    [DllImport("rstrtmgr.dll")]
    private static extern int RmShutdown(uint sessionHandle, int actionFlags, IntPtr statusCallback);

    [DllImport("rstrtmgr.dll")]
    private static extern int RmEndSession(uint sessionHandle);

    internal sealed record RestartManagerResult(IReadOnlyList<LockedProcess> Processes, string? Error)
    {
        internal static RestartManagerResult Success(IReadOnlyList<LockedProcess> processes) => new(processes, null);

        internal static RestartManagerResult Failed(string error) => new([], error);
    }

    internal sealed record LockedProcess(string Name, int ProcessId, string ExecutablePath, string ApplicationType);

    [StructLayout(LayoutKind.Sequential)]
    internal struct RM_UNIQUE_PROCESS
    {
        internal int dwProcessId;
        internal FILETIME ProcessStartTime;
    }

    // Field order and layout must mirror restartmanager.h's RM_PROCESS_INFO exactly:
    // RM_UNIQUE_PROCESS (id + FILETIME) first, then the two inline WCHAR arrays, then the scalars.
    // The previous declaration carried a stray ProcessStartTime field and put the strings last,
    // which shifted every record by 8 bytes -- multi-process results read back wrong PIDs, truncated
    // app names and garbage application types.
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct RM_PROCESS_INFO
    {
        internal RM_UNIQUE_PROCESS Process;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] internal string strAppName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] internal string strServiceShortName;
        internal RM_APP_TYPE ApplicationType;
        internal uint AppStatus;
        internal uint TSSessionId;
        [MarshalAs(UnmanagedType.Bool)] internal bool Restartable;
    }

    internal enum RM_APP_TYPE
    {
        Unknown,
        MainWindow,
        OtherWindow,
        Service,
        Explorer,
        Console,
        Critical
    }

    private enum RM_REBOOT_REASON
    {
        None = 0
    }
}
