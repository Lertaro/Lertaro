using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.App.ViewModels.Settings.Plugins;

namespace Lertaro.App.Services.Plugin;

public class FilteredSidebarFilterProvider : ISidebarFilterProvider
{
    private readonly ISidebarFilterProvider _inner;
    private readonly string _dllName;
    private readonly PluginManager _manager;

    public FilteredSidebarFilterProvider(ISidebarFilterProvider inner, string dllName, PluginManager manager)
    {
        _inner = inner;
        _dllName = dllName;
        _manager = manager;
    }

    // The real plugin-defined provider this wraps -- needed by anything (e.g.
    // SidebarGroupOrderViewModel) that has to compute this provider's identity the same way
    // PluginManager.SidebarFilterProviders' own ordering does, which reads GetType()/assembly off the
    // INNER provider, not this wrapper (whose own GetType() would just say "FilteredSidebarFilterProvider").
    public ISidebarFilterProvider Inner => _inner;

    public int SortOrder => _inner.SortOrder;

    public IEnumerable<SidebarFilterGroup> GetFilterGroups()
    {
        var index = 0;
        foreach (var group in PluginPerformanceMonitor.Measure(_inner, () => _inner.GetFilterGroups()?.ToList() ?? new List<SidebarFilterGroup>()))
        {
            if (_manager.IsComponentEnabled(_dllName, PluginComponentType.FilterProvider, $"{_inner.GetType().Name}_{index}"))
            {
                yield return group;
            }
            index++;
        }
    }
}
