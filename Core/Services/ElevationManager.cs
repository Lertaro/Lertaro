using System.ComponentModel;
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
    public static bool IsProcessHighIntegrity(int pid) => TryGetIntegrityRid(pid) is { } rid && IsHighRid(rid);

    /// <summary>
    /// The process's integrity label, or null when it could not be read. The null is the point: a caller that
    /// reports a level has to be able to tell "measured Medium" apart from "did not look", and an out
    /// parameter that stays 0 when nothing was read gets printed as if it were a measurement.
    /// </summary>
    public static int? TryGetIntegrityRid(int pid) => TryGetProcessIntegrityRid(pid, out var rid) ? rid : null;

    /// <summary>High is the level at which a process's window messages reach an elevated program.</summary>
    public static bool IsHighRid(int rid) => rid >= SECURITY_MANDATORY_HIGH_RID;

    /// <summary>
    /// The process's integrity as a phrase, or why it could not be read. Worth logging instead of a bool:
    /// administrator is a role and High is a label, and a system that reports only the role keeps the label
    /// invisible -- which is how a hook asked for at High could come out Medium with nothing in any log to
    /// say so, while the role line said it was elevated.
    /// </summary>
    public static string DescribeProcessIntegrity(int pid) =>
        TryGetProcessIntegrityRid(pid, out var rid, out var failure)
            ? $"{IntegrityName(rid)} (0x{rid:x})"
            : $"unreadable ({failure})";

    private static bool TryGetProcessIntegrityRid(int pid, out int rid) => TryGetProcessIntegrityRid(pid, out rid, out _);

    private static bool TryGetProcessIntegrityRid(int pid, out int rid, out string? failure)
    {
        rid = 0;
        failure = null;
        var process = IntPtr.Zero;
        var token = IntPtr.Zero;
        try
        {
            // Query-limited access is the one opening that works across integrity levels, which is the whole
            // point of asking this from a normal-integrity process about an elevated one.
            process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (process == IntPtr.Zero)
            {
                failure = Win32Failure();
                return false;
            }
            if (!OpenProcessToken(process, TOKEN_QUERY, out token))
            {
                failure = Win32Failure();
                return false;
            }

            return TryGetTokenIntegrityRid(token, out rid, out failure);
        }
        catch (Exception ex)
        {
            failure = ex.Message;
            Logger.Log($"[ElevationManager] Could not read PID {pid}'s integrity level: {ex.Message}", LogLevel.Debug);
            return false;
        }
        finally
        {
            if (token != IntPtr.Zero) CloseHandle(token);
            if (process != IntPtr.Zero) CloseHandle(process);
        }
    }

    /// <summary>The label SID's last sub-authority: 0x1000 Low, 0x2000 Medium, 0x3000 High, 0x4000 System.</summary>
    public static bool TryGetTokenIntegrityRid(IntPtr token, out int rid) => TryGetTokenIntegrityRid(token, out rid, out _);

    public static bool TryGetTokenIntegrityRid(IntPtr token, out int rid, out string? failure)
    {
        rid = 0;
        failure = null;
        var size = Marshal.SizeOf<TOKEN_MANDATORY_LABEL>();
        // Asked for twice on purpose: TokenIntegrityLevel hands back the label SID next to the struct, so a
        // buffer sized for the struct alone fails with ERROR_BAD_LENGTH -- measured here, and it made every
        // process answer "not High", an elevated one included.
        GetTokenInformation(token, TOKEN_INTEGRITY_LEVEL, IntPtr.Zero, 0, out var needed);
        if (needed < size) needed = size;

        var buffer = Marshal.AllocHGlobal(needed);
        try
        {
            if (!GetTokenInformation(token, TOKEN_INTEGRITY_LEVEL, buffer, needed, out _))
            {
                failure = Win32Failure();
                return false;
            }

            // Read it before the buffer goes away, since the SID lives inside it.
            var sid = Marshal.PtrToStructure<TOKEN_MANDATORY_LABEL>(buffer).Label.Sid;
            var count = Marshal.ReadByte(GetSidSubAuthorityCount(sid));
            if (count == 0)
            {
                failure = "the label SID has no sub-authority";
                return false;
            }

            rid = Marshal.ReadInt32(GetSidSubAuthority(sid, (uint)(count - 1)));
            return true;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public static string IntegrityName(int rid) => rid switch
    {
        SECURITY_MANDATORY_LOW_RID => "Low",
        SECURITY_MANDATORY_MEDIUM_RID => "Medium",
        SECURITY_MANDATORY_HIGH_RID => "High",
        SECURITY_MANDATORY_SYSTEM_RID => "System",
        _ => $"0x{rid:x}",
    };

    private static string Win32Failure() =>
        $"{new Win32Exception(Marshal.GetLastWin32Error()).Message}, Win32 {Marshal.GetLastWin32Error()}";

    private const int TOKEN_INTEGRITY_LEVEL = 25; // TOKEN_INFORMATION_CLASS.TokenIntegrityLevel (WinNT.h)
    private const int SECURITY_MANDATORY_LOW_RID = 0x1000;
    private const int SECURITY_MANDATORY_MEDIUM_RID = 0x2000;
    private const int SECURITY_MANDATORY_HIGH_RID = 0x3000;
    private const int SECURITY_MANDATORY_SYSTEM_RID = 0x4000;
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
        catch (Win32Exception ex)
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
