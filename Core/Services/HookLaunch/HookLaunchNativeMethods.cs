using System.Runtime.InteropServices;

namespace Lertaro.Core.Services.HookLaunch;

// Win32 interop for HookProcessBroker. Kept in its own file (mirrors PipeSecurityFactory) rather than
// inline, since HookProcessBroker already needs the full 300-line budget for the actual token/process logic.
internal static class HookLaunchNativeMethods
{
    public const uint MAXIMUM_ALLOWED = 0x02000000;
    public const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;
    // CREATE_NO_WINDOW still allocates a (hidden) console -- which still spawns a conhost.exe to host it.
    // DETACHED_PROCESS means the child never gets a console at all, so no conhost.exe shows up either.
    // Per CreateProcess docs the two are mutually exclusive (CREATE_NO_WINDOW is ignored if DETACHED_PROCESS
    // is also set), so only this one is used.
    public const uint DETACHED_PROCESS = 0x00000008;
    public const int TokenLinkedToken = 19; // TOKEN_INFORMATION_CLASS.TokenLinkedToken (WinNT.h)
    public const int SecurityImpersonation = 2; // SECURITY_IMPERSONATION_LEVEL.SecurityImpersonation
    public const int TokenPrimary = 1; // TOKEN_TYPE.TokenPrimary

    [StructLayout(LayoutKind.Sequential)]
    public struct TOKEN_LINKED_TOKEN
    {
        public IntPtr LinkedToken;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct STARTUPINFO
    {
        public int cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PROCESS_INFORMATION
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public int dwProcessId;
        public int dwThreadId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID { public uint LowPart; public int HighPart; }

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID_AND_ATTRIBUTES { public LUID Luid; public uint Attributes; }

    [StructLayout(LayoutKind.Sequential)]
    private struct TOKEN_PRIVILEGES { public uint PrivilegeCount; public LUID_AND_ATTRIBUTES Privileges; }

    [DllImport("wtsapi32.dll", SetLastError = true)]
    public static extern bool WTSQueryUserToken(uint sessionId, out IntPtr phToken);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool GetTokenInformation(IntPtr tokenHandle, int tokenInformationClass, IntPtr tokenInformation, int tokenInformationLength, out int returnLength);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool DuplicateTokenEx(IntPtr hExistingToken, uint dwDesiredAccess, IntPtr lpTokenAttributes, int impersonationLevel, int tokenType, out IntPtr phNewToken);

    [DllImport("userenv.dll", SetLastError = true)]
    public static extern bool CreateEnvironmentBlock(out IntPtr lpEnvironment, IntPtr hToken, bool bInherit);

    [DllImport("userenv.dll", SetLastError = true)]
    public static extern bool DestroyEnvironmentBlock(IntPtr lpEnvironment);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool CreateProcessAsUser(IntPtr hToken, string? lpApplicationName, string? lpCommandLine, IntPtr lpProcessAttributes, IntPtr lpThreadAttributes, bool bInheritHandles, uint dwCreationFlags, IntPtr lpEnvironment, string? lpCurrentDirectory, ref STARTUPINFO lpStartupInfo, out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CloseHandle(IntPtr handle);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool LookupPrivilegeValue(string? lpSystemName, string lpName, out LUID luid);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool AdjustTokenPrivileges(IntPtr tokenHandle, bool disableAllPrivileges, ref TOKEN_PRIVILEGES newState, int bufferLength, IntPtr previousState, IntPtr returnLength);

    // WTSQueryUserToken requires SeTcbPrivilege (Act as part of the operating system) to be enabled, not
    // just held -- LocalSystem's token has it but it isn't enabled by default. Safe to call repeatedly;
    // a no-op if already enabled.
    public static void EnableTcbPrivilege()
    {
        const uint TOKEN_ADJUST_PRIVILEGES = 0x0020, TOKEN_QUERY = 0x0008, SE_PRIVILEGE_ENABLED = 0x0002;
        // Kernel32 pseudo-handle: a constant needing no close, unlike a managed Process object's
        // SafeProcessHandle which would wait for a finalizer once the wrapper is dropped.
        var process = Win32Api.GetCurrentProcess();
        if (!OpenProcessToken(process, TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out var token))
            return;
        try
        {
            if (!LookupPrivilegeValue(null, "SeTcbPrivilege", out var luid))
                return;
            var privileges = new TOKEN_PRIVILEGES { PrivilegeCount = 1, Privileges = new LUID_AND_ATTRIBUTES { Luid = luid, Attributes = SE_PRIVILEGE_ENABLED } };
            AdjustTokenPrivileges(token, false, ref privileges, 0, IntPtr.Zero, IntPtr.Zero);
        }
        finally
        {
            CloseHandle(token);
        }
    }
}
