using System.Diagnostics;
using System.IO;
using System.Net.Http;

namespace Lertaro.Plugins.FlowLauncherBridge.Engine.JsonRpc;

/// <summary>
/// Manages pip installation, automated requirements.txt dependency resolution,
/// and FlowPlugins environment stubs. All data is 100% self-contained in FlowPlugins.
/// </summary>
public static class FlowPipManager
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(3) };
    private static bool _pipChecked;
    private static readonly object Lock = new();

    public static string GetFlowPluginsDirectory()
    {
        var baseDir = PluginSdk.Services.UserDataService.GetUserDataDirectory()
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lertaro");
        return Path.Combine(baseDir, "FlowPlugins");
    }

    public static async Task EnsurePipAndRequirementsAsync(string pythonExe, string pluginDir)
    {
        EnsureFlowEnvironmentStubs();

        var reqFile = Path.Combine(pluginDir, "requirements.txt");
        var markerFile = Path.Combine(pluginDir, ".requirements_installed");

        if (!File.Exists(reqFile) || File.Exists(markerFile))
            return;

        await EnsurePipInstalledAsync(pythonExe).ConfigureAwait(false);
        await InstallRequirementsAsync(pythonExe, pluginDir, reqFile, markerFile).ConfigureAwait(false);
    }

    public static void EnsureFlowEnvironmentStubs()
    {
        try
        {
            var flowPluginsDir = GetFlowPluginsDirectory();

            var imagesDir = Path.Combine(flowPluginsDir, "Images");
            Directory.CreateDirectory(imagesDir);

            var settingsDir = Path.Combine(flowPluginsDir, "Settings");
            var pluginsSettingsDir = Path.Combine(settingsDir, "Plugins");
            Directory.CreateDirectory(pluginsSettingsDir);

            var settingsJson = Path.Combine(settingsDir, "Settings.json");
            if (!File.Exists(settingsJson) || !File.ReadAllText(settingsJson).Trim().StartsWith('{'))
            {
                File.WriteAllText(settingsJson, "{\"PluginSettings\":{\"Plugins\":{}}}");
            }
        }
        catch { }
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
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using var process = new Process { StartInfo = psi };
        try
        {
            process.Start();
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
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
