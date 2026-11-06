using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace Lertaro.Core.Services;

public static class ElevationManager
{
    public static bool IsRunningAsAdmin()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Whether the process runs at High integrity, which is the level at which its window messages reach an
    /// elevated program -- so it is the question to ask of the hook before trusting it with an Open/Save
    /// dialog that belongs to one. Anything that cannot be answered counts as not High: the caller then keeps
    /// the process it has rather than restarting it on a guess.
    /// </summary>
    public static bool IsProcessHighIntegrity(int pid)
    {
        var process = IntPtr.Zero;
        var token = IntPtr.Zero;
        var buffer = IntPtr.Zero;
        try
        {
            // Query-limited access is the one opening that works across integrity levels, which is the whole
            // point of asking this from a normal-integrity process about an elevated one.
            process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (process == IntPtr.Zero) return false;
            if (!OpenProcessToken(process, TOKEN_QUERY, out token)) return false;

            var size = Marshal.SizeOf<TOKEN_MANDATORY_LABEL>();
            // Asked for twice on purpose: TokenIntegrityLevel hands back the label SID next to the struct, so
            // a buffer sized for the struct alone fails with ERROR_BAD_LENGTH -- measured, and it made this
            // answer "not High" for every process, including an elevated one.
            GetTokenInformation(token, TOKEN_INTEGRITY_LEVEL, IntPtr.Zero, 0, out var needed);
            if (needed < size) needed = size;
            buffer = Marshal.AllocHGlobal(needed);
            if (!GetTokenInformation(token, TOKEN_INTEGRITY_LEVEL, buffer, needed, out _)) return false;

            // The label SID's last sub-authority is the level: 0x1000 Low, 0x2000 Medium, 0x3000 High,
            // 0x4000 System. Read it before the buffer goes away, since the SID lives inside it.
            var sid = Marshal.PtrToStructure<TOKEN_MANDATORY_LABEL>(buffer).Label.Sid;
            var count = Marshal.ReadByte(GetSidSubAuthorityCount(sid));
            return count > 0
                && Marshal.ReadInt32(GetSidSubAuthority(sid, (uint)(count - 1))) >= SECURITY_MANDATORY_HIGH_RID;
        }
        catch (Exception ex)
        {
            Logger.Log($"[ElevationManager] Could not read PID {pid}'s integrity level: {ex.Message}", LogLevel.Debug);
            return false;
        }
        finally
        {
            if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
            if (token != IntPtr.Zero) CloseHandle(token);
            if (process != IntPtr.Zero) CloseHandle(process);
        }
    }

    private const int TOKEN_INTEGRITY_LEVEL = 25; // TOKEN_INFORMATION_CLASS.TokenIntegrityLevel (WinNT.h)
    private const int SECURITY_MANDATORY_HIGH_RID = 0x3000;
    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    private const uint TOKEN_QUERY = 0x0008;

    [StructLayout(LayoutKind.Sequential)]
    private struct SID_AND_ATTRIBUTES
    {
        public IntPtr Sid;
        public uint Attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TOKEN_MANDATORY_LABEL
    {
        public SID_AND_ATTRIBUTES Label;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(IntPtr TokenHandle, int TokenInformationClass,
        IntPtr TokenInformation, int TokenInformationLength, out int ReturnLength);

    [DllImport("advapi32.dll")]
    private static extern IntPtr GetSidSubAuthorityCount(IntPtr pSid);

    [DllImport("advapi32.dll")]
    private static extern IntPtr GetSidSubAuthority(IntPtr pSid, uint dwIndex);

    public static bool TryElevateProcess(string exePath, string[] args)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = exePath,
                UseShellExecute = true,
                Verb = "runas"
            };

            if (args != null && args.Length > 0)
            {
                startInfo.Arguments = string.Join(" ", args);
            }

            Process.Start(startInfo);
            return true;
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            Logger.Log($"[ElevationManager] UAC elevation prompt was declined: {ex.Message}", LogLevel.Info);
            return false;
        }
        catch (Exception ex)
        {
            Logger.Log($"[ElevationManager] Failed to relaunch elevated: {ex.Message}", LogLevel.Error);
            return false;
        }
    }
}
