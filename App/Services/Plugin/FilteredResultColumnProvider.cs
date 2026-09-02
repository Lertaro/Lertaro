using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Abstractions.Plugins;
using Lertaro.App.ViewModels.Settings.Plugins;

namespace Lertaro.App.Services.Plugin;

public class FilteredResultColumnProvider : IResultColumnProvider
{
    private readonly IResultColumnProvider _inner;
    private readonly string _dllName;
    private readonly PluginManager _manager;

    public FilteredResultColumnProvider(IResultColumnProvider inner, string dllName, PluginManager manager)
    {
        _inner = inner;
        _dllName = dllName;
        _manager = manager;
    }

    public IEnumerable<ResultColumnDefinition> GetColumns()
    {
        foreach (var col in PluginPerformanceMonitor.Measure(_inner, () => _inner.GetColumns()?.ToList() ?? new List<ResultColumnDefinition>()))
        {
            if (_manager.IsComponentEnabled(_dllName, PluginComponentType.ColumnProvider, col.ColumnId))
            {
                yield return col;
            }
        }
    }

    public string GetCellValue(ISearchResult result, string columnId)
        => PluginPerformanceMonitor.Measure(_inner, () => _inner.GetCellValue(result, columnId));
}
