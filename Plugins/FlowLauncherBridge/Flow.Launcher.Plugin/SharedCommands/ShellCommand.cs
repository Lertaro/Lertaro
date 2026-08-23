using System.Diagnostics;

namespace Flow.Launcher.Plugin.SharedCommands;

/// <summary>
/// Contains methods for running shell commands and starting processes.
/// </summary>
public static class ShellCommand
{
    public static ProcessStartInfo SetProcessStartInfo(
        this string fileName,
        string workingDirectory = "",
        string arguments = "",
        string verb = "",
        bool createNoWindow = false)
    {
        return new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            Arguments = arguments,
            Verb = verb,
            CreateNoWindow = createNoWindow,
            UseShellExecute = !createNoWindow && string.IsNullOrEmpty(arguments)
        };
    }

    public static Process? Execute(this ProcessStartInfo info)
    {
        return Process.Start(info);
    }
}
