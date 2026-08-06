namespace Lertaro.Core.Hook.Ipc;

/// <summary>
/// Centralized pipe naming for the hook IPC channel.
/// Each (user, Windows session) pair gets its own pipe name, preventing
/// conflicts between multiple logged-in users or Fast-User-Switching sessions.
/// </summary>
public static class HookIpcNames
{
    public static string EventPipeName =>
        $"Lertaro_Hook_Events_{SanitizeForPipeName(Environment.UserName)}_{GetCurrentSessionId()}";

    public static string CmdPipeName =>
        $"Lertaro_Hook_Cmds_{SanitizeForPipeName(Environment.UserName)}_{GetCurrentSessionId()}";

    private static string SanitizeForPipeName(string value) =>
        // Named pipe names cannot contain backslashes (domain\user); replace with underscore.
        value.Replace('\\', '_').Replace('/', '_');

    private static int GetCurrentSessionId()
    {
        try
        {
            return System.Diagnostics.Process.GetCurrentProcess().SessionId;
        }
        catch
        {
            return 0;
        }
    }
}
