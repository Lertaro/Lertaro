using System.Diagnostics;
using System.IO;
using System.Net.Http;

namespace Lertaro.Plugins.FlowLauncherBridge.Engine.JsonRpc;

/// <summary>
/// Manages pip installation, automated requirements.txt dependency resolution,
/// and FlowData environment stubs. All data is 100% self-contained in FlowData.
/// </summary>
public static class FlowPipManager
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(3) };
    private static bool _pipChecked;
    private static readonly object Lock = new();
    // Serializes requirements installs: a concurrent Ensure while one is running must not run a
    // second pip over the same lib directory (check-then-act on the marker without this raced two
    // pip processes into one target tree).
    private static readonly SemaphoreSlim InstallGate = new(1, 1);

    public static string GetFlowDataDirectory()
    {
        var baseDir = PluginSdk.Services.UserDataService.GetUserDataDirectory()
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lertaro");
        return Path.Combine(baseDir, "FlowData");
    }

    public static string GetFlowPluginsDirectory() => Path.Combine(GetFlowDataDirectory(), "Plugins");

    public static void EnsurePipAndRequirementsBackground(string pythonExe, string pluginDir)
    {
        var reqFile = Path.Combine(pluginDir, "requirements.txt");
        var markerFile = Path.Combine(pluginDir, ".requirements_installed");

        if (!File.Exists(reqFile) || File.Exists(markerFile))
            return;

        _ = Task.Run(async () =>
        {
            if (!await InstallGate.WaitAsync(0).ConfigureAwait(false))
                return;
            try
            {
                await EnsurePipInstalledAsync(pythonExe).ConfigureAwait(false);
                await InstallRequirementsAsync(pythonExe, pluginDir, reqFile, markerFile).ConfigureAwait(false);
            }
            finally
            {
                InstallGate.Release();
            }
        });
    }

    public static async Task EnsurePipAndRequirementsAsync(string pythonExe, string pluginDir)
    {
        var reqFile = Path.Combine(pluginDir, "requirements.txt");
        var markerFile = Path.Combine(pluginDir, ".requirements_installed");

        if (!File.Exists(reqFile) || File.Exists(markerFile))
            return;

        await EnsurePipInstalledAsync(pythonExe).ConfigureAwait(false);
        await InstallRequirementsAsync(pythonExe, pluginDir, reqFile, markerFile).ConfigureAwait(false);
    }

    public static async Task EnsurePipInstalledAsync(string pythonExe)
    {
        lock (Lock)
        {
            if (_pipChecked) return;
        }

        if (await RunProcessAsync(pythonExe, "-m pip --version").ConfigureAwait(false))
        {
            lock (Lock) { _pipChecked = true; }
            return;
        }

        var tempGetPip = Path.Combine(Path.GetTempPath(), $"get_pip_{Guid.NewGuid():N}.py");
        try
        {
            using (var response = await Http.GetAsync("https://bootstrap.pypa.io/get-pip.py").ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                await using var fs = new FileStream(tempGetPip, FileMode.Create, FileAccess.Write, FileShare.None);
                await response.Content.CopyToAsync(fs).ConfigureAwait(false);
            }

            await RunProcessAsync(pythonExe, $"\"{tempGetPip}\" --no-warn-script-location").ConfigureAwait(false);
            lock (Lock) { _pipChecked = true; }
        }
        catch { }
        finally
        {
            try { if (File.Exists(tempGetPip)) File.Delete(tempGetPip); } catch { }
        }
    }

    private static async Task InstallRequirementsAsync(string pythonExe, string pluginDir, string reqFile, string markerFile)
    {
        try
        {
            var libDir = Path.Combine(pluginDir, "lib");
            Directory.CreateDirectory(libDir);

            var args = $"-m pip install -r \"{reqFile}\" --target \"{libDir}\" --no-warn-script-location";
            var success = await RunProcessAsync(pythonExe, args, pluginDir).ConfigureAwait(false);
            if (success)
            {
                await File.WriteAllTextAsync(markerFile, DateTime.UtcNow.ToString("O")).ConfigureAwait(false);
            }
        }
        catch { }
    }

    private static async Task<bool> RunProcessAsync(string exe, string arguments, string? workingDir = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = arguments,
            WorkingDirectory = workingDir ?? Path.GetDirectoryName(exe) ?? string.Empty,
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
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            // Drain both pipes WHILE waiting: pip writes far more output than the ~4KB pipe buffer
            // holds, and a child blocked on a full stdout pipe never exits -- a wait-only version
            // deadlocked here until the timeout killed pip mid-install, leaving a half-populated
            // lib directory behind.
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
