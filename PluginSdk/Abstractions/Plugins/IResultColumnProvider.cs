namespace Lertaro.PluginSdk.Abstractions.Plugins;

/// <summary>
/// Plugin interface to register and provide cell values for custom columns in the results GridView.
/// </summary>
public interface IResultColumnProvider : IPluginComponent
{
    /// <summary>
    /// Returns the custom columns defined by this provider.
    /// </summary>
    IEnumerable<ResultColumnDefinition> GetColumns();

    /// <summary>
    /// Provides a cell value for a specific result and column ID.
    /// </summary>
    string GetCellValue(ISearchResult result, string columnId);
}

/// <summary>
/// Represents a custom column definition.
/// </summary>
public class ResultColumnDefinition
{
    public string ColumnId { get; set; } = string.Empty;
    public string HeaderText { get; set; } = string.Empty;
    public double Width { get; set; } = 120;

    /// <summary>
    /// Optional predicate to determine if this column should be visible for a search result context.
    /// </summary>
    public Func<ISearchResult, bool>? VisibilityPredicate { get; set; }

    /// <summary>
    /// Optional custom comparer to sort by this column.
    /// Returns negative if x &lt; y, zero if x == y, positive if x &gt; y.
    /// </summary>
    public Func<ISearchResult, ISearchResult, int>? SortComparer { get; set; }

    /// <summary>
    /// Optional handler for a double-click on this column's cell in the full window's results grid.
    /// Leave unset (the default) to keep double-clicking this column identical to double-clicking
    /// anywhere else on the row (opens the result).
    /// </summary>
    public Action<ISearchResult>? OnDoubleClick { get; set; }
}
