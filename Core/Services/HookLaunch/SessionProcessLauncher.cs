using System.Runtime.InteropServices;
using System.Security.Principal;

using static Lertaro.Core.Services.HookLaunch.HookLaunchNativeMethods;

namespace Lertaro.Core.Services.HookLaunch;

/// <summary>
/// Starts a process inside someone else's logon session, as that session's own user.
/// </summary>
/// <remarks>
/// Only the LocalSystem <c>--service</c> process can do this: <c>WTSQueryUserToken</c> needs
/// SeTcbPrivilege, and handing the child the session's token rather than the service's own is what keeps
/// a launched process from being an artifact of the service's privileges. <paramref name="requestElevation"/>
/// swaps in the UAC-linked admin token, which is how an elevated child is obtained without ever showing a
/// consent prompt -- the session user is genuinely an administrator, so there is no one to ask. When that
/// isn't true the plain token is used instead, so a request can only ever downgrade, never escalate.
///
/// Lifted out of <see cref="HookProcessBroker"/>, which layers "one live hook per session" on top of this.
/// The update applier needs the same launch with different bookkeeping -- a detached applier must never be
/// deduped away because a hook happens to be running.
/// </remarks>
internal static class SessionProcessLauncher
{
    /// <param name="detachFromConsole">
    /// True for a child that never touches a terminal (the hook). False gives it a console with no window
    /// instead, which is what a batch script needs: <c>timeout</c> and the like refuse to run when no
    /// console exists at all, and the updater is a batch script. The conhost that comes with it is hidden.
    /// </param>
    public static bool TryLaunch(int sessionId, string exePath, string arguments, bool requestElevation, bool detachFromConsole, out int pid, out string? error)
    {
        pid = 0;
        error = null;

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
                // request for elevation still gets a working child at the normal level, never a hard failure.
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
            var creationFlags = CREATE_UNICODE_ENVIRONMENT | (detachFromConsole ? DETACHED_PROCESS : CREATE_NO_WINDOW);

            if (!CreateProcessAsUser(primaryToken, null, commandLine, IntPtr.Zero, IntPtr.Zero, false,
                    creationFlags, envBlock, null, ref startupInfo, out var processInfo))
            {
                error = $"CreateProcessAsUser failed (error {Marshal.GetLastWin32Error()}).";
                return false;
            }

            pid = processInfo.dwProcessId;
            if (processInfo.hProcess != IntPtr.Zero) CloseHandle(processInfo.hProcess);
            if (processInfo.hThread != IntPtr.Zero) CloseHandle(processInfo.hThread);

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
