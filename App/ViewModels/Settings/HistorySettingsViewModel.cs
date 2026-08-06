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

    public HistorySettingsViewModel(UserSettings userSettings)
    {
        _userSettings = userSettings;

        SearchHistory = new HistoryListViewModel<HistoryEntry>(
            () => SearchHistoryStore.GetEntries().ToList(),
            MapSearchEntry,
            () => _userSettings.EnableHistory,
            v => _userSettings.EnableHistory = v);

        KeywordHistory = new HistoryListViewModel<string>(
            KeywordHistoryStore.GetEntries,
            MapKeywordEntry,
            () => _userSettings.EnableKeywordHistory,
            v => _userSettings.EnableKeywordHistory = v);
    }

    public string SelectedTab
    {
        get => _selectedTab;
        set => SetProperty(ref _selectedTab, value);
    }

    public ICommand SelectTabCommand => _selectTabCommand ??= new RelayCommand<string>(tab => SelectedTab = tab);

    public HistoryListViewModel<HistoryEntry> SearchHistory { get; }
    public HistoryListViewModel<string> KeywordHistory { get; }

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
        var isVirtual = entry.Path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase) || entry.Path.StartsWith("::", StringComparison.Ordinal);
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
            IconGlyph = iconGlyph
        };
    }

    private static HistoryEntryViewModel<string> MapKeywordEntry(string keyword) => new()
    {
        RawValue = keyword,
        Primary = keyword,
        Secondary = string.Empty,
        IconGlyph = KeywordIconGlyph
    };

    public void Save()
    {
        SearchHistoryStore.SaveEntries(SearchHistory.GetEntriesToSave());
        KeywordHistoryStore.SaveEntries(KeywordHistory.GetEntriesToSave());
    }
}
