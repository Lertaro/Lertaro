namespace Lertaro.App.ViewModels.QuickPanel;

// The box at the top of the panel. Split out of QuickPanelViewModel.cs purely to keep that file under
// the repo's per-file line limit; it has no state of its own beyond the query.
public partial class QuickPanelViewModel
{
    private string _searchQuery = string.Empty;

    /// <summary>Narrows every group on screen to what matches, hiding the ones left with nothing.</summary>
    /// <remarks>
    /// A filter over what the workspace already loaded, not a search: the panel's whole premise is that
    /// these particular folders are the ones worth having to hand, and a box that reached past them
    /// would be the main search window wearing a smaller frame.
    ///
    /// It narrows what is on screen and nothing else. A workspace whose every entry the filter rejects
    /// keeps its tab: the strip says which workspaces have something in them, and having it flicker as
    /// each keystroke lands would make it say something different and much less useful.
    /// </remarks>
    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (!SetProperty(ref _searchQuery, value)) return;
            ApplyFilter();
        }
    }

    /// <summary>Empties the box, which is also what Escape does while there is anything in it.</summary>
    public System.Windows.Input.ICommand ClearSearchCommand
        => _clearSearch ??= new Helpers.RelayCommand(() => SearchQuery = string.Empty);

    private System.Windows.Input.ICommand? _clearSearch;

    private void ApplyFilter()
    {
        foreach (var group in Groups)
            group.ApplyFilter(_searchQuery);

        // Under a filter, "empty" means nothing matched rather than nothing loaded -- which is the more
        // useful thing for the panel to say, and the only one the user can act on.
        IsEmpty = !Groups.Any(group => group.HasMatches);
        UpdateLineNumberSizing();
    }
}
