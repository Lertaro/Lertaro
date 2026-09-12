using System.IO;
using System.Windows.Input;
using Lertaro.App.Helpers;
using Lertaro.Core;
using Lertaro.PluginSdk.Helpers;
using Lertaro.PluginSdk.Services;

namespace Lertaro.App.ViewModels.Settings;

public class HistorySettingsViewModel : ViewModelBase
{
    private readonly UserSettings _userSettings;
    private string _selectedTab = "Search";
    private ICommand? _selectTabCommand;

    // Debounces the two stores' Changed events: Record can fire several times in quick succession
    // (e.g. opening a folder then a file from it in one burst), and rebuilding both lists every time
    // churns the UI. One short timer collapses the burst into a single refresh.
    private readonly System.Windows.Threading.DispatcherTimer _refreshDebounce = new()
    {
        Interval = TimeSpan.FromMilliseconds(150)
    };

    public HistorySettingsViewModel(UserSettings userSettings)
    {
        _userSettings = userSettings;

        SearchHistory = new HistoryListViewModel<HistoryEntry>(
            () => SearchHistoryStore.GetEntries().ToList(),
            MapSearchEntry,
            () => _userSettings.EnableHistory,
            v => _userSettings.EnableHistory = v);

        KeywordHistory = new HistoryListViewModel<KeywordHistoryEntry>(
            KeywordHistoryStore.GetCountedEntries,
            MapKeywordEntry,
            () => _userSettings.EnableKeywordHistory,
            v => _userSettings.EnableKeywordHistory = v);

        // Live counts: the store raises Changed on every Record/Save/Delete, so the visible rows stay
        // in step with what the user just opened in any search window -- no manual refresh needed.
        // Record runs on a background thread (SearchHistoryStore.Record's Task.Run), so the refresh
        // must hop onto the UI thread before touching the bound ObservableCollection.
        _refreshDebounce.Tick += (_, _) =>
        {
            _refreshDebounce.Stop();
            SearchHistory.RefreshFromStore();
            KeywordHistory.RefreshFromStore();
        };
        SearchHistoryStore.Changed += OnStoreChanged;
        KeywordHistoryStore.Changed += OnStoreChanged;
    }

    private void OnStoreChanged()
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher == null)
            return;

        // Restart the timer on the UI thread; the burst settles before the single rebuild runs.
        dispatcher.BeginInvoke(new Action(() =>
        {
            _refreshDebounce.Stop();
            _refreshDebounce.Start();
        }));
    }

    public string SelectedTab
    {
        get => _selectedTab;
        set => SetProperty(ref _selectedTab, value);
    }

    public ICommand SelectTabCommand => _selectTabCommand ??= new RelayCommand<string>(tab => SelectedTab = tab);

    public HistoryListViewModel<HistoryEntry> SearchHistory { get; }
    public HistoryListViewModel<KeywordHistoryEntry> KeywordHistory { get; }

    // Segoe MDL2 Assets glyphs (U+E160 Page2, U+E8B7 Folder, U+E737 AppIconDefault, U+E81C). These are
    // private-use-area characters, invisible in a plain-text diff/editor view -- a hand-retyped edit
    // previously replaced them with empty strings without the change looking any different, which
    // silently blanked every icon in this list. Codepoints noted here so the same slip is easy to catch.
    private const string FileIconGlyph = "";
    private const string FolderIconGlyph = "";
    private const string ApplicationIconGlyph = "";
    private const string KeywordIconGlyph = "";

    private static HistoryEntryViewModel<HistoryEntry> MapSearchEntry(HistoryEntry entry)
    {
        var isVirtual = UserPathResolver.IsVirtualPath(entry.Path);
        var primary = isVirtual
            ? ShellPathHelper.GetVirtualFolderDisplayName(entry.Path, entry.Path)
            : (Path.GetFileName(entry.Path) is { Length: > 0 } name ? name : entry.Path);
        var iconGlyph = entry.Kind switch
        {
            HistoryEntryKind.Folder => FolderIconGlyph,
            HistoryEntryKind.Application => ApplicationIconGlyph,
            _ => FileIconGlyph
        };

        return new HistoryEntryViewModel<HistoryEntry>
        {
            RawValue = entry,
            Primary = primary,
            Secondary = entry.Path,
            IconGlyph = iconGlyph,
            UsageCount = entry.Count
        };
    }

    private static HistoryEntryViewModel<KeywordHistoryEntry> MapKeywordEntry(KeywordHistoryEntry entry) => new()
    {
        RawValue = entry,
        Primary = entry.Keyword,
        Secondary = string.Empty,
        IconGlyph = KeywordIconGlyph,
        UsageCount = entry.Count
    };

    public void Save()
    {
        SearchHistoryStore.SaveEntries(SearchHistory.GetEntriesToSave());
        KeywordHistoryStore.SaveEntries(KeywordHistory.GetEntriesToSave());
    }

    public void NotifyLanguageChanged()
    {
        SearchHistory.NotifyLanguageChanged();
        KeywordHistory.NotifyLanguageChanged();
    }

    public void Cleanup()
    {
        _refreshDebounce.Stop();
        SearchHistoryStore.Changed -= OnStoreChanged;
        KeywordHistoryStore.Changed -= OnStoreChanged;
    }
}
