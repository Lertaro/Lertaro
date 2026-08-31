using System.Windows.Input;
using Lertaro.App.Helpers;
using Lertaro.PluginSdk.Abstractions;
using Lertaro.PluginSdk.Abstractions.Plugins;

namespace Lertaro.App.ViewModels.Search.DynamicSidebar;

public class DynamicSidebarGroupViewModel : ViewModelBase
{
    private readonly SearchViewModel _mainVm;

    public DynamicSidebarGroupViewModel(SidebarFilterGroup group, SearchViewModel mainVm)
    {
        _mainVm = mainVm;
        Id = group.Id;
        _header = group.Header;
        AllowMultiSelect = group.AllowMultiSelect;
        Items = group.Items.Select(item => new DynamicSidebarItemViewModel(item, this)).ToList();
        // Nothing selected by default -- there's no "All" pseudo-item anymore; an empty selection IS
        // the unfiltered state.
    }

    public string Id { get; }

    private string _header;
    public string Header
    {
        get => _header;
        private set => SetProperty(ref _header, value);
    }
    public bool AllowMultiSelect { get; }
    public List<DynamicSidebarItemViewModel> Items { get; }

    // group.Header above is a one-time translated snapshot (the provider resolved it via
    // TranslationService.Get when SearchViewModel's constructor called GetFilterGroups()), so it goes
    // stale on a language switch. Called from SearchViewModel.RefreshDynamicSidebarLabels with a freshly
    // re-resolved value.
    internal void UpdateHeader(string header) => Header = header;

    private bool _isFirst;
    public bool IsFirst
    {
        get => _isFirst;
        set => SetProperty(ref _isFirst, value);
    }

    public bool HasSelection => Items.Any(i => i.IsSelected);

    // Combines every currently-selected item's predicate with OR: a result survives if it matches ANY
    // of them. Null (not an identity no-op) when nothing is selected, so the caller can tell "this
    // group contributes no filter at all" apart from "this group's filter happens to keep everything".
    public Func<IReadOnlyList<ISearchResult>, Task<IReadOnlyList<ISearchResult>>>? CombinedPredicate
    {
        get
        {
            var selected = Items.Where(i => i.IsSelected).ToList();
            if (selected.Count == 0)
                return null;
            if (selected.Count == 1)
                return results => Task.FromResult<IReadOnlyList<ISearchResult>>(
                    results.Where(selected[0].MatchPredicate).ToList());

            return results => Task.FromResult<IReadOnlyList<ISearchResult>>(
                results.Where(result => selected.Any(item => item.MatchPredicate(result))).ToList());
        }
    }

    // Called by an item's IsSelected setter, and by ClearSelection below -- the single place that
    // enforces "at most one selected item" for a non-multi-select group and fires exactly one
    // OnDynamicFilterChanged per user action (not once per sibling silently cleared).
    internal void OnItemSelectionChanged(DynamicSidebarItemViewModel changed)
    {
        if (changed.IsSelected && !AllowMultiSelect)
        {
            foreach (var item in Items)
            {
                if (item != changed)
                    item.SetSelectedSilently(false);
            }
        }
        OnPropertyChanged(nameof(HasSelection));
        _mainVm.OnDynamicFilterChanged();
    }

    private ICommand? _clearCommand;
    public ICommand ClearCommand => _clearCommand ??= new RelayCommand(ClearSelection);

    private void ClearSelection()
    {
        var anyChanged = false;
        foreach (var item in Items)
            anyChanged |= item.SetSelectedSilently(false);

        if (!anyChanged)
            return;

        OnPropertyChanged(nameof(HasSelection));
        _mainVm.OnDynamicFilterChanged();
    }
}

public class DynamicSidebarItemViewModel : ViewModelBase
{
    private readonly SidebarFilterItem _item;
    public DynamicSidebarGroupViewModel Group { get; }

    public DynamicSidebarItemViewModel(SidebarFilterItem item, DynamicSidebarGroupViewModel group)
    {
        _item = item;
        Group = group;
        _displayName = item.DisplayName;
    }

    public string Id => _item.Id;

    private string _displayName;
    public string DisplayName
    {
        get => _displayName;
        private set => SetProperty(ref _displayName, value);
    }

    // _item.DisplayName is a one-time translated snapshot, same staleness issue as Header above -- see
    // SearchViewModel.RefreshDynamicSidebarLabels.
    internal void UpdateDisplayName(string displayName) => DisplayName = displayName;

    public string IconString => !string.IsNullOrEmpty(_item.IconKey) ? _item.IconKey : "◆";
    public string? IconData => _item.IconData;
    public bool HasIconData => !string.IsNullOrEmpty(_item.IconData);
    internal Func<ISearchResult, bool> MatchPredicate => _item.MatchPredicate;

    private int _count;
    public int Count
    {
        get => _count;
        private set => SetProperty(ref _count, value);
    }

    private bool _hasCount;
    public string CountText => _hasCount ? _count.ToString("N0") : string.Empty;

    internal void SetCount(int count)
    {
        var changed = SetProperty(ref _count, count, nameof(Count));
        if (!_hasCount)
        {
            _hasCount = true;
            changed = true;
        }
        if (changed)
            OnPropertyChanged(nameof(CountText));
    }

    internal void ClearCount()
    {
        if (!_hasCount)
            return;
        _hasCount = false;
        OnPropertyChanged(nameof(CountText));
    }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            OnPropertyChanged();
            Group.OnItemSelectionChanged(this);
        }
    }

    // Sets the backing field directly (raises the property-changed notification but does NOT call back
    // into Group.OnItemSelectionChanged) -- used when the GROUP is the one driving the change (clearing
    // siblings for a single-select group, or a full group clear), so that one user action fires exactly
    // one OnDynamicFilterChanged instead of one per item touched. Returns whether it actually changed.
    internal bool SetSelectedSilently(bool value)
    {
        if (_isSelected == value) return false;
        _isSelected = value;
        OnPropertyChanged(nameof(IsSelected));
        return true;
    }
}
