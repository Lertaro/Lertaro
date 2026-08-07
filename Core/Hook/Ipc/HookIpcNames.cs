using Lertaro.Core.Services.Installation;

namespace Lertaro.Core.Hook.Ipc;

/// <summary>
/// Centralized pipe naming for the hook IPC channel.
/// Each SID-and-session hash gets its own pipe name, preventing
/// conflicts between multiple logged-in users or Fast-User-Switching sessions.
/// </summary>
public static class HookIpcNames
{
    public static string EventPipeName =>
        BuildName("Events", CurrentUserIdentity.SessionHash);

    public static string CmdPipeName =>
        BuildName("Cmds", CurrentUserIdentity.SessionHash);

    internal static string BuildName(string channel, string sessionHash)
        => $"Lertaro_Hook_{channel}_{sessionHash}";
}
