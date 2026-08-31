using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;

using static Lertaro.Core.Services.HookLaunch.HookLaunchNativeMethods;

namespace Lertaro.Core.Services.HookLaunch;

// Runs inside the SYSTEM-privileged --service process. Every hook launch -- elevated or not -- goes
// through here rather than the App spawning its own child process, so the App never has to hold a
// runas/UAC fallback of its own. Launches into the requesting session with that session's own token:
// plain (non-elevated) by default, or the UAC-linked admin token when requestElevation is set AND the
// session's user is genuinely an administrator. A non-admin session (or requestElevation=false) just
// gets a normal, non-elevated hook -- same privilege level it would have gotten launching itself.
// Tracks one live hook process per session so repeated requests don't spawn duplicates.
public static class HookProcessBroker
{
    private static readonly ConcurrentDictionary<int, Process> _liveHooks = new();
    private static readonly object _liveHooksGate = new();

    public static bool TryLaunch(int sessionId, string exePath, string arguments, bool requestElevation, out int pid, out string? error)
    {
        pid = 0;
        error = null;

        lock (_liveHooksGate)
        {
        if (_liveHooks.TryGetValue(sessionId, out var existing))
        {
            try
            {
                if (!existing.HasExited)
                {
                    pid = existing.Id;
                    return true;
                }
            }
            catch { /* process object stale; fall through and relaunch */ }
            if (_liveHooks.TryRemove(sessionId, out var removed))
                removed.Dispose();
        }

        EnableTcbPrivilege();

        var userToken = IntPtr.Zero;
        var linkedToken = IntPtr.Zero;
        var primaryToken = IntPtr.Zero;
        var envBlock = IntPtr.Zero;
        try
        {
            if (!WTSQueryUserToken((uint)sessionId, out userToken))
            {
                error = $"WTSQueryUserToken failed (session {sessionId}, error {Marshal.GetLastWin32Error()}).";
                return false;
            }

            var launchToken = userToken;
            if (requestElevation)
            {
                // Only actually elevates when the session's user is genuinely an administrator;
                // otherwise silently falls through to the plain token below -- a non-admin (or spoofed)
                // request for elevation still gets a working non-elevated hook, never a hard failure.
                if (TryGetLinkedToken(userToken, out linkedToken) && IsTokenAdmin(linkedToken))
                    launchToken = linkedToken;
                else if (IsTokenAdmin(userToken))
                    launchToken = userToken; // UAC disabled but genuinely an admin account
            }

            if (!DuplicateTokenEx(launchToken, MAXIMUM_ALLOWED, IntPtr.Zero, SecurityImpersonation, TokenPrimary, out primaryToken))
            {
                error = $"DuplicateTokenEx failed (error {Marshal.GetLastWin32Error()}).";
                return false;
            }

            if (!CreateEnvironmentBlock(out envBlock, primaryToken, false))
            {
                error = $"CreateEnvironmentBlock failed (error {Marshal.GetLastWin32Error()}).";
                return false;
            }

            var startupInfo = new STARTUPINFO { cb = Marshal.SizeOf<STARTUPINFO>(), lpDesktop = @"winsta0\default" };
            var commandLine = $"\"{exePath}\" {arguments}";

            if (!CreateProcessAsUser(primaryToken, null, commandLine, IntPtr.Zero, IntPtr.Zero, false,
                    CREATE_UNICODE_ENVIRONMENT | DETACHED_PROCESS, envBlock, null, ref startupInfo, out var processInfo))
            {
                error = $"CreateProcessAsUser failed (error {Marshal.GetLastWin32Error()}).";
                return false;
            }

            try
            {
                pid = processInfo.dwProcessId;
                var newProcess = Process.GetProcessById(pid);
                if (_liveHooks.TryGetValue(sessionId, out var previous))
                    previous.Dispose();
                _liveHooks[sessionId] = newProcess;
            }
            finally
            {
                if (processInfo.hProcess != IntPtr.Zero) CloseHandle(processInfo.hProcess);
                if (processInfo.hThread != IntPtr.Zero) CloseHandle(processInfo.hThread);
            }

            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
        finally
        {
            if (envBlock != IntPtr.Zero) DestroyEnvironmentBlock(envBlock);
            if (primaryToken != IntPtr.Zero) CloseHandle(primaryToken);
            if (linkedToken != IntPtr.Zero) CloseHandle(linkedToken);
            if (userToken != IntPtr.Zero) CloseHandle(userToken);
        }
    }
    }

    private static bool TryGetLinkedToken(IntPtr token, out IntPtr linkedToken)
    {
        linkedToken = IntPtr.Zero;
        var size = Marshal.SizeOf<TOKEN_LINKED_TOKEN>();
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            // Fails when UAC is off or the account has no linked elevated token -- not fatal, caller
            // just proceeds with the original token (non-admin, or already-elevated-by-default accounts).
            if (!GetTokenInformation(token, TokenLinkedToken, buffer, size, out _))
                return false;

            linkedToken = Marshal.PtrToStructure<TOKEN_LINKED_TOKEN>(buffer).LinkedToken;
            return linkedToken != IntPtr.Zero;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static bool IsTokenAdmin(IntPtr token)
    {
        try
        {
            using var identity = new WindowsIdentity(token);
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }
}
