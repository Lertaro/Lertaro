using System.Collections.ObjectModel;
using System.Windows.Input;
using Lertaro.App.Helpers;
using Lertaro.App.Services;

using Lertaro.Core.SearchIndex;
namespace Lertaro.App.ViewModels.Settings;

/// <summary>
/// Backs the reusable history list UI (search box, scrollable entries, remove/clear, enable toggle).
/// Shared by the "search history" and "keyword history" tabs -- each supplies its own storage and how
/// a raw stored entry (a <see cref="PluginSdk.Services.HistoryEntry"/> for search history, a
/// <see cref="KeywordHistoryEntry"/> for keyword history) maps to a displayable row, and gets it back
/// unchanged from <see cref="GetEntriesToSave"/> to persist.
/// </summary>
public class HistoryListViewModel<T> : ViewModelBase
{
    private readonly Func<IReadOnlyList<T>> _loadEntries;
    private readonly Func<T, HistoryEntryViewModel<T>> _mapEntry;
    private readonly Func<bool> _getEnabled;
    private readonly Action<bool> _setEnabled;
    private readonly List<HistoryEntryViewModel<T>> _allItems = new();
    private string _searchText = string.Empty;

    public HistoryListViewModel(
        Func<IReadOnlyList<T>> loadEntries,
        Func<T, HistoryEntryViewModel<T>> mapEntry,
        Func<bool> getEnabled,
        Action<bool> setEnabled)
    {
        _loadEntries = loadEntries;
        _mapEntry = mapEntry;
        _getEnabled = getEnabled;
        _setEnabled = setEnabled;

        foreach (var raw in _loadEntries())
            _allItems.Add(_mapEntry(raw));

        FilteredItems = new ObservableCollection<HistoryEntryViewModel<T>>(_allItems);
        RemoveItemCommand = new RelayCommand<HistoryEntryViewModel<T>>(RemoveItem);
        ClearAllCommand = new RelayCommand(ClearAll);
        ClearBelowCommand = new RelayCommand(ClearBelow);
    }

    public bool IsHistoryEnabled
    {
        get => _getEnabled();
        set
        {
            if (_getEnabled() != value)
            {
                _setEnabled(value);
                OnPropertyChanged();
            }
        }
    }

    public ObservableCollection<HistoryEntryViewModel<T>> FilteredItems { get; }
    public ICommand RemoveItemCommand { get; }
    public ICommand ClearAllCommand { get; }
    public ICommand ClearBelowCommand { get; }

    // Threshold for "clear records used at most N times". Fixed to a 1..20 dropdown so the user can
    // never type an out-of-range or non-numeric value; the clear rule is "at most" (<=), not "fewer than".
    public IReadOnlyList<int> UsageThresholds { get; } = Enumerable.Range(1, 20).ToList();

    private int _usageThreshold = 1;
    public int UsageThreshold
    {
        get => _usageThreshold;
        set
        {
            if (SetProperty(ref _usageThreshold, value))
                OnPropertyChanged(nameof(ClearBelowButtonLabel));
        }
    }

    public string ClearBelowButtonLabel => string.Format(
        TranslationManager.Instance["Settings_History_Clear_Below"],
        UsageThreshold);

    public void NotifyLanguageChanged() => OnPropertyChanged(nameof(ClearBelowButtonLabel));

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
                ApplyFilter();
        }
    }

    private void RemoveItem(HistoryEntryViewModel<T>? item)
    {
        if (item == null) return;
        _allItems.Remove(item);
        FilteredItems.Remove(item);
    }

    private void ClearAll()
    {
        _allItems.Clear();
        FilteredItems.Clear();
    }

    private void ClearBelow()
    {
        // "At most N times" (<=), from the fixed 1..20 dropdown so the value is always in range.
        _allItems.RemoveAll(item => item.UsageCount <= UsageThreshold);
        FilteredItems.Clear();
        foreach (var item in _allItems)
            FilteredItems.Add(item);
    }

    /// <summary>Re-reads the backing store and rebuilds the rows, keeping the current filter/search.</summary>
    public void RefreshFromStore()
    {
        _allItems.Clear();
        foreach (var raw in _loadEntries())
            _allItems.Add(_mapEntry(raw));
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        FilteredItems.Clear();
        foreach (var item in _allItems)
        {
            // FuzzyMatcher.ComputeBestMatch (same FzfPattern.Parse Core's file search uses) splits a
            // multi-word SearchText into independently-required terms -- a plain .Contains(SearchText)
            // treated the whole typed text (spaces included) as one literal string, so a query like
            // "foo bar" would never match an entry containing both words non-contiguously.
            if (string.IsNullOrEmpty(SearchText) ||
                FuzzyMatcher.ComputeBestMatch(SearchText, item.Primary, new[] { item.Secondary }).IsMatch)
            {
                FilteredItems.Add(item);
            }
        }
    }

    /// <summary>Returns the current entries (in their edited order) for the caller to persist.</summary>
    public IEnumerable<T> GetEntriesToSave() => _allItems.Select(x => x.RawValue);
}

/// <summary>One row in the history list -- a file/folder/app path (with a subtitle) or a bare keyword.</summary>
public class HistoryEntryViewModel<T> : ViewModelBase
{
    public required T RawValue { get; init; }
    public string Primary { get; init; } = string.Empty;
    public string Secondary { get; init; } = string.Empty;
    public string IconGlyph { get; init; } = "";
    public int UsageCount { get; init; } = 1;
}
