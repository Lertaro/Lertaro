namespace Lertaro.PluginSdk.Abstractions.Plugins;

/// <summary>
/// Plugin interface to register custom filter categories and items in the Search Window sidebar.
/// </summary>
public interface ISidebarFilterProvider : IPluginComponent
{
    /// <summary>
    /// Returns the filter groups to be displayed in the sidebar.
    /// </summary>
    IEnumerable<SidebarFilterGroup> GetFilterGroups();

    /// <summary>
    /// Ordering weight. Lower values render first.
    /// </summary>
    int SortOrder => 100;
}

/// <summary>
/// Represents a group of sidebar filter items.
/// </summary>
public class SidebarFilterGroup
{
    public string Header { get; set; } = string.Empty;
    public List<SidebarFilterItem> Items { get; set; } = new();

    /// <summary>
    /// Whether more than one item in this group can be selected at once, combined with OR (a result
    /// matching ANY selected item's <see cref="SidebarFilterItem.MatchPredicate"/> is kept). Leave
    /// false for items whose meaning only makes sense one at a time (e.g. overlapping/cumulative date
    /// ranges) -- the host still lets the user clear the group's selection entirely regardless.
    /// </summary>
    public bool AllowMultiSelect { get; set; }
}

/// <summary>
/// Represents a filter item in a sidebar group.
/// </summary>
public class SidebarFilterItem
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Vector icon path geometry (optional).
    /// </summary>
    public string? IconData { get; set; }

    /// <summary>
    /// Key used for UI icon matching if vector path is not supplied (optional).
    /// </summary>
    public string? IconKey { get; set; }

    /// <summary>
    /// Returns whether one result belongs to this filter. The host uses this same predicate both
    /// while counting the streamed search results and when applying the selected filter, so counts
    /// cannot drift from the rows that a filter actually keeps.
    /// </summary>
    public Func<ISearchResult, bool> MatchPredicate { get; set; } = _ => false;
}
