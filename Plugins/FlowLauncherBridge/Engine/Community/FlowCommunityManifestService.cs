using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using Lertaro.PluginSdk.Services;

namespace Lertaro.Plugins.FlowLauncherBridge.Engine.Community;

/// <summary>
/// Fetches and caches the official Flow.Launcher community plugins manifest.
/// </summary>
public static class FlowCommunityManifestService
{
    private static readonly string[] ManifestUrls =
    [
        "https://fastly.jsdelivr.net/gh/Flow-Launcher/Flow.Launcher.PluginsManifest@main/plugins.json",
        "https://gcore.jsdelivr.net/gh/Flow-Launcher/Flow.Launcher.PluginsManifest@main/plugins.json",
        "https://cdn.jsdelivr.net/gh/Flow-Launcher/Flow.Launcher.PluginsManifest@main/plugins.json",
        "https://raw.githubusercontent.com/Flow-Launcher/Flow.Launcher.PluginsManifest/main/plugins.json"
    ];

    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    private static readonly SemaphoreSlim FetchLock = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static List<FlowCommunityPlugin>? _cachedPlugins;
    private static DateTime _lastFetchTime = DateTime.MinValue;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(10);
    private static int _isFetching;

    public static bool IsFetching => Volatile.Read(ref _isFetching) == 1;

    public static IReadOnlyList<FlowCommunityPlugin>? GetCachedPlugins()
    {
        if (_cachedPlugins != null && DateTime.UtcNow - _lastFetchTime < CacheDuration)
            return _cachedPlugins;

        return null;
    }

    public static void TriggerBackgroundFetch(string triggerKeyword, string requestQuery)
    {
        if (Interlocked.CompareExchange(ref _isFetching, 1, 0) != 0)
            return;

        Task.Run(async () =>
        {
            try
            {
                var plugins = await FetchManifestAsync().ConfigureAwait(false);
                if (plugins != null && plugins.Count > 0)
                {
                    _cachedPlugins = plugins;
                    _lastFetchTime = DateTime.UtcNow;
                }
            }
            catch
            {
                // Silently swallow fetch errors; caller can retry
            }
            finally
            {
                Interlocked.Exchange(ref _isFetching, 0);
                SearchRefreshService.RefreshIfMatches(currentQuery =>
                    currentQuery.StartsWith(triggerKeyword, StringComparison.OrdinalIgnoreCase));
            }
        });
    }

    public static async Task<List<FlowCommunityPlugin>?> FetchManifestAsync(CancellationToken token = default)
    {
        await FetchLock.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (_cachedPlugins != null && DateTime.UtcNow - _lastFetchTime < CacheDuration)
                return _cachedPlugins;

            foreach (var url in ManifestUrls)
            {
                try
                {
                    using var response = await HttpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
                    if (response.IsSuccessStatusCode)
                    {
                        var list = await response.Content.ReadFromJsonAsync<List<FlowCommunityPlugin>>(JsonOptions, token).ConfigureAwait(false);
                        if (list != null && list.Count > 0)
                        {
                            _cachedPlugins = list;
                            _lastFetchTime = DateTime.UtcNow;
                            return list;
                        }
                    }
                }
                catch
                {
                    // Try next mirror
                }
            }

            return _cachedPlugins;
        }
        finally
        {
            FetchLock.Release();
        }
    }
}
