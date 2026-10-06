using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;
using Lertaro.Core.Services.HookLaunch;

namespace Lertaro.Core.Services.Pipe;

// Identifies the process/session on the other end of a connected NamedPipeServerStream straight from the
// kernel handle, so privileged handlers can verify who's actually asking instead of trusting anything the
// client claims in the request payload.
internal static partial class PipeClientIdentity
{
    public static bool IsAuthorizedApp(NamedPipeServerStream pipe, bool administrator = false)
    {
        try
        {
            if (!TryGetClientProcessId(pipe, out var pid) || !HookLaunchRequestHandler.IsGenuineAppProcess(pid))
                return false;
            return !administrator || IsAdministratorAccount(pid);
        }
        catch { return false; }
    }

    internal static bool IsAdministratorAccount(int pid)
    {
        using var process = OpenProcess(0x1000, 0, pid); // PROCESS_QUERY_LIMITED_INFORMATION
        if (process.IsInvalid || OpenProcessToken(process, 0x000A, out var token) == 0) return false; // QUERY | DUPLICATE
        using (token)
        {
            using var identity = new WindowsIdentity(token.DangerousGetHandle());
            if (new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator)) return true;
            // TokenElevationTypeLimited means this is an administrator's UAC-filtered token. The
            // linked token returned to a non-SYSTEM process may have QUERY only; IsInRole would need
            // DUPLICATE and reject the ordinary App even when its account is an administrator.
            return GetTokenInformation(token, 18, out var elevationType, sizeof(int), out _) != 0 && elevationType == 3;
        }
    }

    internal static bool IsCurrentUsersProcess(Process process)
    {
        using var currentProcess = Process.GetCurrentProcess();
        if (process.SessionId != currentProcess.SessionId ||
            OpenProcessToken(process.SafeHandle, 0x0008, out var token) == 0) return false;
        using (token)
        using (var identity = new WindowsIdentity(token.DangerousGetHandle()))
        using (var current = WindowsIdentity.GetCurrent())
            return identity.User == current.User;
    }

    [LibraryImport("advapi32.dll", SetLastError = true)]
    private static partial int OpenProcessToken(SafeProcessHandle process, uint access, out SafeAccessTokenHandle token);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    private static partial int GetTokenInformation(SafeAccessTokenHandle token, int informationClass, out int information,
        int length, out int returnLength);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial SafeProcessHandle OpenProcess(uint access, int inherit, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNamedPipeClientProcessId(SafeHandle pipe, out uint clientProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNamedPipeClientSessionId(SafeHandle pipe, out uint clientSessionId);

    public static bool TryGetClientProcessId(NamedPipeServerStream pipe, out int pid)
    {
        if (GetNamedPipeClientProcessId(pipe.SafePipeHandle, out var raw))
        {
            pid = (int)raw;
            return true;
        }
        pid = 0;
        return false;
    }

    public static bool TryGetClientSessionId(NamedPipeServerStream pipe, out int sessionId)
    {
        if (GetNamedPipeClientSessionId(pipe.SafePipeHandle, out var raw))
        {
            sessionId = (int)raw;
            return true;
        }
        sessionId = 0;
        return false;
    }
}
