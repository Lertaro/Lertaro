using System.IO.Pipes;
using System.Runtime.InteropServices;

namespace Lertaro.Core.Hook.Ipc;

/// <summary>
/// Asks the OS which process serves the other end of a connected client pipe, so the App can confirm it
/// reached the hook it launched. The names in <see cref="HookIpcNames"/> are derived from the user's
/// identity and session rather than being secret, and nothing stops another process answering the name
/// first -- the hook asks for one server instance, so its own create simply fails -- which would hand
/// that process the App's Explorer/inline-search state and its tool-run channel.
/// </summary>
internal static class HookPipePeer
{
    /// <summary>
    /// Whether a connection whose server is owned by <paramref name="serverPid"/> is really the hook
    /// process identified by <paramref name="launchedHookPid"/>. A null <paramref name="serverPid"/>
    /// means the OS could not answer (already-disconnected handle, unsupported configuration), and a
    /// zero <paramref name="launchedHookPid"/> means there is nothing to compare against; neither is
    /// evidence of an impostor, so both are accepted rather than breaking the product's own hook.
    /// </summary>
    internal static bool IsImpersonation(int? serverPid, int launchedHookPid) =>
        serverPid.HasValue && launchedHookPid != 0 && serverPid.Value != launchedHookPid;

    /// <summary>The PID owning the pipe server behind <paramref name="pipe"/>, or null when unknown.</summary>
    internal static int? TryGetServerProcessId(NamedPipeClientStream pipe)
    {
        try
        {
            return GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var serverProcessId) && serverProcessId != 0
                ? (int)serverProcessId
                : null;
        }
        catch
        {
            // Every failure here means "unknown", never "mismatch"; the caller decides what an
            // unknown peer is worth. A security decision must not hinge on an API throwing.
            return null;
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNamedPipeServerProcessId(SafeHandle pipe, out uint serverProcessId);
}
