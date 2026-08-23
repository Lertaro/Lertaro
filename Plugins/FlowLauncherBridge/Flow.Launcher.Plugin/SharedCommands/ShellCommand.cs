using System.Diagnostics;

namespace Flow.Launcher.Plugin.SharedCommands;

public static class ShellCommand
{
    public delegate bool EnumThreadDelegate(IntPtr hwnd, IntPtr lParam);

    public static void Execute(string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return;
        try
        {
            Process.Start(new ProcessStartInfo(command) { UseShellExecute = true });
        }
        catch { }
    }

    public static void Execute(ProcessStartInfo info)
    {
        Execute(Process.Start!, info);
    }

    public static void Execute(Func<ProcessStartInfo, Process> startProcess, ProcessStartInfo info)
    {
        startProcess?.Invoke(info);
    }

    public static Process? RunAsDifferentUser(ProcessStartInfo processStartInfo)
    {
        processStartInfo.Verb = "RunAsUser";
        return Process.Start(processStartInfo);
    }

    public static ProcessStartInfo SetProcessStartInfo(this string fileName, string workingDirectory = "", string arguments = "", string verb = "", bool createNoWindow = false)
    {
        return new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
            Arguments = arguments,
            Verb = verb,
            CreateNoWindow = createNoWindow
        };
    }
}
