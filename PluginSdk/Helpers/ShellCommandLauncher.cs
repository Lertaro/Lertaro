using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Lertaro.PluginSdk.Helpers;

/// <summary>Starts a command interpreter, preserving the caller's directory and elevation choice.</summary>
public static class ShellCommandLauncher
{
    public static string? GetShellName(string command) => command.Trim().ToLowerInvariant() switch
    {
        "cmd" => "cmd",
        "powershell" => "powershell",
        "pwsh" => "pwsh",
        _ => null
    };

    /// <summary>Current directories are literal paths; only configured paths expand environment variables.</summary>
    public static string? ResolveWorkingDirectory(string? configuredDirectory, bool useCurrentDirectory, string? contextDirectory)
        => ResolveWorkingDirectory(configuredDirectory, useCurrentDirectory, contextDirectory, Directory.Exists);

    internal static string? ResolveWorkingDirectory(string? configuredDirectory, bool useCurrentDirectory, string? contextDirectory, Func<string, bool> exists)
    {
        var directory = useCurrentDirectory ? contextDirectory : UserPathResolver.Expand(configuredDirectory);
        if (!useCurrentDirectory && string.IsNullOrWhiteSpace(directory)) return null;
        if (string.IsNullOrWhiteSpace(directory) || !Path.IsPathFullyQualified(directory) || !exists(directory))
            throw new DirectoryNotFoundException($"Working directory is unavailable: {directory}");
        return Path.GetFullPath(directory);
    }

    public static void Launch(string command, string shell = "cmd", bool runAsAdmin = false, string? workingDirectory = null)
    {
        var directory = ResolveWorkingDirectory(null, workingDirectory != null, workingDirectory);
        LaunchCore(command, shell, runAsAdmin, directory, ResolveExecutable, info => Process.Start(info));
    }

    internal static void LaunchCore(string command, string shell, bool runAsAdmin, string? directory,
        Func<string, string?> resolve, Action<ProcessStartInfo> start)
    {
        var directShell = GetShellName(command);
        var selectedShell = directShell ?? GetShellName(shell) ?? throw new ArgumentException("Unknown command interpreter.", nameof(shell));
        var executable = resolve(selectedShell);
        if (executable != null)
        {
            try
            {
                start(BuildStartInfo(executable, selectedShell, directShell != null ? "" : command, runAsAdmin, directory));
                return;
            }
            // Only an exact shell name may fall back, and only if the executable disappeared.
            // UAC cancellation (1223), access denied, and other launch failures must never retry.
            catch (Win32Exception ex) when (directShell != null && ex.NativeErrorCode is 2 or 3)
            {
                if (resolve(selectedShell) != null) throw; // A missing directory/dependency is not a missing interpreter.
            }
        }
        else if (directShell == null)
        {
            throw new FileNotFoundException($"Command interpreter was not found: {selectedShell}");
        }

        var cmd = resolve("cmd") ?? throw new FileNotFoundException("Command Prompt was not found.");
        start(BuildStartInfo(cmd, "cmd", command, runAsAdmin, directory));
    }

    internal static ProcessStartInfo BuildStartInfo(string executable, string shell, string command, bool runAsAdmin, string? directory)
    {
        var info = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = true,
            WorkingDirectory = directory ?? "",
            Verb = runAsAdmin ? "runas" : ""
        };
        if (shell == "cmd")
        {
            // CMD's cd/pushd strips these empty quotes, while its percent-expansion pass cannot
            // interpret the original %NAME% in a directory as an environment-variable reference.
            // /v:off also keeps literal ! characters, and pushd supports UNC directories.
            // Stop the interpreter if a directory disappears after validation, including when the
            // user's command contains an independent '&' or '||' clause.
            // ponytail: keep CMD's native one-line parsing. %CD% expands before pushd if Windows
            // overrides the initial directory (elevation/UNC); use PowerShell's $PWD in that case.
            var initialize = directory == null ? "" : $"pushd \"{directory.Replace("%", "%\"\"")}\" || exit";
            info.Arguments = "/d /v:off /k " + (initialize.Length == 0 ? command
                : command.Length == 0 ? initialize : initialize + " & " + command);
        }
        else
        {
            var script = directory == null ? command
                : $"Set-Location -LiteralPath '{directory.Replace("'", "''")}' -ErrorAction Stop;\n{command}";
            info.ArgumentList.Add("-NoExit");
            if (script.Length > 0)
            {
                info.ArgumentList.Add("-EncodedCommand");
                info.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(script)));
            }
        }
        return info;
    }

    private static string? ResolveExecutable(string shell)
    {
        if (shell is "cmd" or "powershell")
        {
            var path = shell == "cmd" ? Path.Combine(Environment.SystemDirectory, "cmd.exe")
                : Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
            return File.Exists(path) ? path : null;
        }

        // Respect installed/portable pwsh on PATH, without searching the command's working directory.
        foreach (var entry in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            var directory = entry.Trim().Trim('"');
            if (!Path.IsPathFullyQualified(directory)) continue;
            var path = Path.Combine(directory, "pwsh.exe");
            if (File.Exists(path)) return path;
        }
        var installed = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PowerShell", "7", "pwsh.exe");
        return File.Exists(installed) ? installed : null;
    }
}
