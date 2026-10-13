using Lertaro.App.Services.Plugin;
using Lertaro.Core;
using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Abstractions.Plugins;

namespace Lertaro.App.ViewModels.Search;

/// <summary>Asks one quick-launch source what it holds, through the cache that keeps a burst of summons cheap.</summary>
/// <remarks>
/// Split out of <see cref="QuickSearchLaunchSourceSupport"/>: this is the half of a summon that talks to a
/// provider -- a folder read, a COM resolve per Recent entry, an IPC round trip -- and it is the only part
/// that is worth a test of its own, which it gets by being handed a provider rather than a live plugin layer.
/// </remarks>
internal static class QuickLaunchSourceReader
{
    internal static async Task<IReadOnlyList<ISearchResult>> LoadProviderAsync(
        string id, IQuickPanelTabProvider provider, CancellationToken token)
    {
        if (QuickLaunchSourceCache.TryGet(id, out var cached))
            return cached;

        try
        {
            var items = await PluginPerformanceMonitor.MeasureAsync(provider, () => provider.GetEntriesAsync(token));
            // An empty answer is never remembered, and neither is one that lost its race: a source can
            // legitimately answer nothing before the service it needs is up, and a superseded load may have
            // stopped early. Both are shown; neither is allowed to keep the tab missing for the TTL.
            if (!token.IsCancellationRequested && items.Count > 0)
                QuickLaunchSourceCache.Set(id, items);
            return items;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return Array.Empty<ISearchResult>();
        }
        catch (Exception ex)
        {
            Logger.Log($"[QuickLaunch] Failed to load source '{provider.GetType().Name}': {ex.Message}", LogLevel.Warn);
            return Array.Empty<ISearchResult>();
        }
    }
}
