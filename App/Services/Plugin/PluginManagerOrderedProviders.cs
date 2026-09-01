using Lertaro.App.Helpers;
using Lertaro.App.Services.PluginManagerCore;
using Lertaro.App.ViewModels.Settings.Plugins;
using Lertaro.Core;
using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.PluginSdk.Abstractions.Plugins.Preview;
using Lertaro.PluginSdk.Abstractions.Plugins.WindowAdapters;

namespace Lertaro.App.Services.Plugin;

/// <summary>
/// Owns the ordered and filtered provider views exposed by <see cref="PluginManager"/>.
/// Split out to keep PluginManager under the repository's per-file line limit; the provider
/// collections still belong to the manager and this class only composes their public views.
/// </summary>
internal sealed class PluginManagerOrderedProviders
{
    private readonly PluginManager _manager;
    private readonly ComponentFilter _filter;
    private readonly List<IQuickNavigationProvider> _quickNavigationProviders;
    private readonly List<ISidebarFilterProvider> _sidebarFilterProviders;
    private readonly List<IResultColumnProvider> _resultColumnProviders;
    private readonly List<IFilePreviewProvider> _previewProviders;
    private readonly List<IThumbnailProvider> _thumbnailProviders;

    internal PluginManagerOrderedProviders(
        PluginManager manager,
        ComponentFilter filter,
        List<IQuickNavigationProvider> quickNavigationProviders,
        List<ISidebarFilterProvider> sidebarFilterProviders,
        List<IResultColumnProvider> resultColumnProviders,
        List<IFilePreviewProvider> previewProviders,
        List<IThumbnailProvider> thumbnailProviders)
    {
        _manager = manager;
        _filter = filter;
        _quickNavigationProviders = quickNavigationProviders;
        _sidebarFilterProviders = sidebarFilterProviders;
        _resultColumnProviders = resultColumnProviders;
        _previewProviders = previewProviders;
        _thumbnailProviders = thumbnailProviders;
    }

    internal IEnumerable<IQuickNavigationProvider> QuickNavigationProviders
    {
        get
        {
            var order = UserSettings.Load().QuickNavigationProviderOrder;
            return _quickNavigationProviders
                .Where(p => IsEnabled(p, PluginComponentType.QuickNavigationProvider))
                .OrderBy(p => Rank(p, PluginComponentType.QuickNavigationProvider, order));
        }
    }

    internal IEnumerable<ISidebarFilterProvider> SidebarFilterProviders
    {
        get
        {
            var order = UserSettings.Load().SidebarGroupOrder;
            return _sidebarFilterProviders
                .OrderBy(p => Rank(p, PluginComponentType.FilterProvider, order))
                .ThenBy(p => p.SortOrder)
                .Select(p => (ISidebarFilterProvider)new FilteredSidebarFilterProvider(
                    p, ComponentFilter.GetDllName(p), _manager));
        }
    }

    internal IEnumerable<IResultColumnProvider> ResultColumnProviders
        => _resultColumnProviders.Select(p => (IResultColumnProvider)new FilteredResultColumnProvider(
            p, ComponentFilter.GetDllName(p), _manager));

    internal IEnumerable<IFilePreviewProvider> FilePreviewProviders
    {
        get
        {
            var order = UserSettings.Load().FilePreviewProviderOrder;
            return _previewProviders
                .Where(p => IsEnabled(p, PluginComponentType.FilePreviewProvider))
                .OrderBy(p => Rank(p, PluginComponentType.FilePreviewProvider, order))
                .ThenByDescending(p => p.Priority);
        }
    }

    internal IEnumerable<IThumbnailProvider> ThumbnailProviders
    {
        get
        {
            var order = UserSettings.Load().ThumbnailProviderOrder;
            return _thumbnailProviders
                .Where(p => IsEnabled(p, PluginComponentType.ThumbnailProvider))
                .OrderBy(p => Rank(p, PluginComponentType.ThumbnailProvider, order))
                .ThenByDescending(p => p.Priority);
        }
    }

    private bool IsEnabled(IPluginComponent component, PluginComponentType type)
        => _filter.IsEnabled(ComponentFilter.GetDllName(component), type, component.GetType().Name);

    private static int Rank(IPluginComponent component, PluginComponentType type, List<string> order)
    {
        var id = PluginLoaderHelper.MakeId(ComponentFilter.GetDllName(component), type, component.GetType().Name);
        var rank = order.IndexOf(id);
        return rank >= 0 ? rank : int.MaxValue;
    }
}
