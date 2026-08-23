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

            var targetPluginDir = Path.Combine(pluginsBaseDir, plugin.Name);
            await host.UnloadPluginAsync(plugin.ID).ConfigureAwait(false);
            if (Directory.Exists(targetPluginDir))
            {
                try { Directory.Delete(targetPluginDir, true); } catch { }
            }

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
            try { if (Directory.Exists(tempExtractDir)) Directory.Delete(tempExtractDir, true); } catch { }
            SearchRefreshService.RefreshIfMatches(q => true);
        }
    }

    private static string ResolvePluginFolder(string extractDir)
    {
        if (File.Exists(Path.Combine(extractDir, "plugin.json")))
            return extractDir;

        var subDirs = Directory.GetDirectories(extractDir);
        if (subDirs.Length == 1 && File.Exists(Path.Combine(subDirs[0], "plugin.json")))
            return subDirs[0];

        foreach (var dir in subDirs)
        {
            if (File.Exists(Path.Combine(dir, "plugin.json")))
                return dir;
        }

        return extractDir;
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
            if (!string.IsNullOrEmpty(metadata.PluginDirectory) && Directory.Exists(metadata.PluginDirectory))
            {
                try { Directory.Delete(metadata.PluginDirectory, true); } catch { }
            }
            SearchRefreshService.RefreshIfMatches(q => true);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
