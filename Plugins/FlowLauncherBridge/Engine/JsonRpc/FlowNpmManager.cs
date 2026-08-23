using System.Diagnostics;
using System.IO;

namespace Lertaro.Plugins.FlowLauncherBridge.Engine.JsonRpc;

/// <summary>
/// Manages automated npm dependency resolution (package.json) for Flow JavaScript/TypeScript plugins.
/// Executes npm via embedded node.exe and npm-cli.js to avoid console window flashing.
/// </summary>
public static class FlowNpmManager
{
    public static void EnsureNpmAndPackagesBackground(string nodeExe, string pluginDir)
    {
        var packageJson = Path.Combine(pluginDir, "package.json");
        var markerFile = Path.Combine(pluginDir, ".npm_installed");
        var nodeModulesDir = Path.Combine(pluginDir, "node_modules");

        if (!File.Exists(packageJson) || File.Exists(markerFile) || Directory.Exists(nodeModulesDir))
            return;

        _ = Task.Run(async () =>
        {
            await InstallPackagesAsync(nodeExe, pluginDir, markerFile).ConfigureAwait(false);
        });
    }

    public static async Task<bool> InstallPackagesAsync(string nodeExe, string pluginDir, string markerFile)
    {
        try
        {
            var nodeDir = Path.GetDirectoryName(nodeExe) ?? string.Empty;
            var npmCliJs = Path.Combine(nodeDir, "node_modules", "npm", "bin", "npm-cli.js");

            string fileName;
            string args;

            if (File.Exists(npmCliJs))
            {
                fileName = nodeExe;
                args = $"\"{npmCliJs}\" install --omit=dev --no-audit --no-fund";
            }
            else
            {
                var npmCmd = Path.Combine(nodeDir, "npm.cmd");
                if (File.Exists(npmCmd))
                {
                    fileName = npmCmd;
                    args = "install --omit=dev --no-audit --no-fund";
                }
                else
                {
                    return false;
                }
            }

            var success = await RunProcessAsync(fileName, args, pluginDir).ConfigureAwait(false);
            if (success)
            {
                await File.WriteAllTextAsync(markerFile, DateTime.UtcNow.ToString("O")).ConfigureAwait(false);
            }
            return success;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<bool> RunProcessAsync(string exe, string arguments, string workingDir)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = arguments,
            WorkingDirectory = workingDir,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using var process = new Process { StartInfo = psi };
        try
        {
            process.Start();
            process.StandardInput.Close();
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
            return process.ExitCode == 0;
        }
        catch
        {
            try { process.Kill(); } catch { }
            return false;
        }
    }
}
