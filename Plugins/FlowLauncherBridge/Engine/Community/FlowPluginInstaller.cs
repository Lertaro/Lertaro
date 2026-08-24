using System.Collections.Concurrent;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using Flow.Launcher.Plugin;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.FlowLauncherBridge.Engine.Community;

/// <summary>
/// Handles downloading, extracting, installing, and dynamically loading Flow.Launcher community plugins.
/// </summary>
public static class FlowPluginInstaller
{
    private static readonly ConcurrentDictionary<string, bool> InstallingPlugins = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(60) };

    public static bool IsInstalling(string pluginId) => InstallingPlugins.ContainsKey(pluginId);

    public static async Task<bool> DownloadAndInstallPluginAsync(
        FlowCommunityPlugin plugin,
        FlowPluginHost host,
        CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(plugin.UrlDownload))
            return false;

        InstallingPlugins[plugin.ID] = true;
        SearchRefreshService.RefreshIfMatches(q => true);

        var tempZipPath = Path.Combine(Path.GetTempPath(), $"{plugin.Name}-{Guid.NewGuid():N}.zip");
        var tempExtractDir = Path.Combine(Path.GetTempPath(), $"flow-extract-{Guid.NewGuid():N}");

        try
        {
            using var response = await HttpClient.GetAsync(plugin.UrlDownload, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return false;

            await using (var fs = new FileStream(tempZipPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await response.Content.CopyToAsync(fs, token).ConfigureAwait(false);
            }

            ZipFile.ExtractToDirectory(tempZipPath, tempExtractDir, true);

            var pluginFolder = ResolvePluginFolder(tempExtractDir);
            var manifestPath = Path.Combine(pluginFolder, "plugin.json");
            if (!File.Exists(manifestPath))
                return false;

            var userDataDir = UserDataService.GetUserDataDirectory()
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lertaro");
            var pluginsBaseDir = Path.Combine(userDataDir, "FlowData", "Plugins");
            Directory.CreateDirectory(pluginsBaseDir);

            var standardDir = Path.Combine(pluginsBaseDir, plugin.Name);
            var existingPlugin = host.GetAllPlugins().FirstOrDefault(p => string.Equals(p.Metadata.ID, plugin.ID, StringComparison.OrdinalIgnoreCase));
            var existingDir = existingPlugin?.Metadata.PluginDirectory;

            await host.UnloadPluginAsync(plugin.ID).ConfigureAwait(false);

            if (!string.IsNullOrEmpty(existingDir) && Directory.Exists(existingDir))
            {
                SafeDeleteDirectory(existingDir);
            }
            SafeDeleteDirectory(standardDir);

            var targetPluginDir = Directory.Exists(standardDir)
                ? Path.Combine(pluginsBaseDir, $"{plugin.Name}-{Guid.NewGuid().ToString("N")[..8]}")
                : standardDir;

            CopyDirectory(pluginFolder, targetPluginDir);

            var targetManifest = Path.Combine(targetPluginDir, "plugin.json");
            return await host.LoadPluginFromDirectoryAsync(targetPluginDir, targetManifest).ConfigureAwait(false);
        }
        catch
        {
            return false;
        }
        finally
        {
            InstallingPlugins.TryRemove(plugin.ID, out _);
            try { if (File.Exists(tempZipPath)) File.Delete(tempZipPath); } catch { }
            SafeDeleteDirectory(tempExtractDir);
            SearchRefreshService.RefreshIfMatches(q => true);
        }
    }

    private static string ResolvePluginFolder(string extractDir)
    {
        var manifest = Directory.GetFiles(extractDir, "plugin.json", SearchOption.AllDirectories).FirstOrDefault();
        return manifest != null ? Path.GetDirectoryName(manifest)! : extractDir;
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source))
        {
            var destFile = Path.Combine(destination, Path.GetFileName(file));
            File.Copy(file, destFile, true);
        }
        foreach (var dir in Directory.GetDirectories(source))
        {
            var destSub = Path.Combine(destination, Path.GetFileName(dir));
            CopyDirectory(dir, destSub);
        }
    }

    public static async Task<bool> UninstallPluginAsync(PluginMetadata metadata, FlowPluginHost host)
    {
        try
        {
            await host.UnloadPluginAsync(metadata.ID).ConfigureAwait(false);

            var userDataDir = UserDataService.GetUserDataDirectory()
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lertaro");
            var pluginsBaseDir = Path.Combine(userDataDir, "FlowData", "Plugins");

            var targetDir = !string.IsNullOrEmpty(metadata.PluginDirectory) && Directory.Exists(metadata.PluginDirectory)
                ? metadata.PluginDirectory
                : Path.Combine(pluginsBaseDir, metadata.Name);

            SafeDeleteDirectory(targetDir);
            SearchRefreshService.RefreshIfMatches(q => true);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static void SafeDeleteDirectory(string directory)
    {
        if (!Directory.Exists(directory)) return;

        try
        {
            foreach (var file in Directory.GetFiles(directory, "*", SearchOption.AllDirectories))
            {
                try
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                    File.Delete(file);
                }
                catch { }
            }
            Directory.Delete(directory, true);
        }
        catch
        {
            try
            {
                var marker = Path.Combine(directory, ".deleted");
                if (!File.Exists(marker))
                {
                    File.WriteAllText(marker, DateTime.UtcNow.ToString("o"));
                }
            }
            catch { }
        }
    }
}
