using System.Diagnostics;
using System.IO;

namespace Lertaro.Plugins.FlowLauncherBridge.Engine.JsonRpc;

/// <summary>
/// Manages automated npm dependency resolution (package.json) for Flow JavaScript/TypeScript plugins.
/// Executes npm via embedded node.exe and npm-cli.js to avoid console window flashing.
/// </summary>
public static class FlowNpmManager
{
    // Serializes npm installs: a concurrent Ensure while one is running must not delete the
    // node_modules being written or run a second npm over the same tree.
    private static readonly SemaphoreSlim InstallGate = new(1, 1);

    public static void EnsureNpmAndPackagesBackground(string nodeExe, string pluginDir)
    {
        var packageJson = Path.Combine(pluginDir, "package.json");
        var markerFile = Path.Combine(pluginDir, ".npm_installed");
        var nodeModulesDir = Path.Combine(pluginDir, "node_modules");

        // The marker is the only "installed" signal. node_modules alone used to count as done too,
        // which permanently masked a half-install left behind when npm was killed mid-run: the
        // marker never got written, the directory existed, and every later load skipped the install.
        if (!File.Exists(packageJson) || File.Exists(markerFile))
            return;

        _ = Task.Run(async () =>
        {
            if (!await InstallGate.WaitAsync(0).ConfigureAwait(false))
                return;
            try
            {
                // Leftover tree from a previous killed install: clear it so the reinstall starts
                // clean instead of npm merging into a possibly corrupt dependency tree.
                if (Directory.Exists(nodeModulesDir))
                {
                    try { Directory.Delete(nodeModulesDir, recursive: true); }
                    catch { /* best-effort; npm reinstalls over what it can */ }
                }
                await InstallPackagesAsync(nodeExe, pluginDir, markerFile).ConfigureAwait(false);
            }
            finally
            {
                InstallGate.Release();
            }
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
            // Drain both pipes WHILE waiting: npm writes far more progress output than the ~4KB
            // pipe buffer holds, and a child blocked on a full stdout pipe never exits -- a
            // wait-only version deadlocked here until the timeout killed npm mid-install, which
            // is also what left half-written node_modules trees behind.
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            var exitTask = process.WaitForExitAsync(cts.Token);
            await Task.WhenAll(exitTask, outputTask, errorTask).ConfigureAwait(false);
            return process.ExitCode == 0;
        }
        catch
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            return false;
        }
    }
}
