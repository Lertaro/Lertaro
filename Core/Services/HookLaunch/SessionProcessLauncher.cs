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
public static class SessionProcessLauncher
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
        var filteredEnvBlock = IntPtr.Zero;
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

            // Keyed on the request, not on which token won: with UAC off an administrator's plain token is
            // already the elevated one, and that launch needs the same cleaning.
            var environment = envBlock;
            if (requestElevation)
            {
                filteredEnvBlock = Marshal.StringToHGlobalUni(FilterEnvironment(ReadEnvironmentBlock(envBlock), PinnedFromThisProcess()));
                environment = filteredEnvBlock;
            }

            var startupInfo = new STARTUPINFO { cb = Marshal.SizeOf<STARTUPINFO>(), lpDesktop = @"winsta0\default" };
            var commandLine = $"\"{exePath}\" {arguments}";
            var creationFlags = CREATE_UNICODE_ENVIRONMENT | (detachFromConsole ? DETACHED_PROCESS : CREATE_NO_WINDOW);

            if (!CreateProcessAsUser(primaryToken, null, commandLine, IntPtr.Zero, IntPtr.Zero, false,
                    creationFlags, environment, null, ref startupInfo, out var processInfo))
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
            if (filteredEnvBlock != IntPtr.Zero) Marshal.FreeHGlobal(filteredEnvBlock);
            if (envBlock != IntPtr.Zero) DestroyEnvironmentBlock(envBlock);
            if (primaryToken != IntPtr.Zero) CloseHandle(primaryToken);
            if (linkedToken != IntPtr.Zero) CloseHandle(linkedToken);
            if (userToken != IntPtr.Zero) CloseHandle(userToken);
        }
    }

    // Variables the .NET runtime reads at start-up to load extra code: startup hooks, profilers, a different
    // runtime or host. The user can set any of them in HKCU\Environment without elevation, so an elevated
    // child inheriting them would load the user's code at high integrity with no UAC prompt.
    private static readonly string[] RuntimeHookPrefixes = ["DOTNET_", "COMPlus_", "CORECLR_", "COR_"];

    // Also user-overridable, and what cmd.exe and the updater script resolve system paths and commands
    // through. Pinned to this service's own (machine) values rather than dropped, since those are always set.
    // ponytail: PATH is left as the user has it -- the hook may start the user's own tools by name -- so a
    // DLL an elevated child fails to find in the system directories is still searched for along the user's
    // PATH. Replacing PATH with the machine PATH for elevated launches is the upgrade.
    private static readonly string[] PinnedVariables = ["SystemRoot", "windir", "ComSpec", "PATHEXT"];

    /// <summary>
    /// The environment for an elevated child: <paramref name="block"/> (NUL-separated <c>NAME=value</c>
    /// entries, double-NUL terminated, as CreateEnvironmentBlock returns it) without the runtime-hook
    /// variables, with <paramref name="pinned"/> replacing any same-named entries, sorted by name the way
    /// CreateProcess expects.
    /// </summary>
    internal static string FilterEnvironment(string block, IReadOnlyDictionary<string, string> pinned)
    {
        // Entries such as "=C:=C:\dir" (per-drive current directories) start with '=', so the name ends at
        // the first '=' after the first character.
        static string NameOf(string entry) => entry[..Math.Max(entry.IndexOf('=', 1), 0)];

        var kept = block.Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Where(entry => !RuntimeHookPrefixes.Any(prefix => entry.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            .Where(entry => !pinned.ContainsKey(NameOf(entry)))
            .Concat(pinned.Select(pair => $"{pair.Key}={pair.Value}"))
            .OrderBy(NameOf, StringComparer.OrdinalIgnoreCase);
        return string.Join('\0', kept) + "\0\0";
    }

    private static Dictionary<string, string> PinnedFromThisProcess()
    {
        var pinned = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in PinnedVariables)
        {
            if (Environment.GetEnvironmentVariable(name) is { Length: > 0 } value)
                pinned[name] = value;
        }

        return pinned;
    }

    private static string ReadEnvironmentBlock(IntPtr block)
    {
        var entries = new List<string>();
        for (var cursor = block; Marshal.PtrToStringUni(cursor) is { Length: > 0 } entry; cursor += (entry.Length + 1) * sizeof(char))
            entries.Add(entry);
        return string.Join('\0', entries) + "\0\0";
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
