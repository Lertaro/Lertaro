using System.Collections.ObjectModel;
using System.Windows.Input;
using Lertaro.App.Helpers;
using Lertaro.Core;

namespace Lertaro.App.ViewModels.Settings.General;

// Edits the alias -> item-name table SearchableItemMapper reads when a query is exactly one of the words
// the user registered. Rows stage here and only reach UserSettings.SettingsItemAliases when Save() runs
// (called from GeneralSettingsApplier), same as the order lists beside this one.
// One word per row: the table is a dictionary, so a word already taken by a row above it is dropped on
// Save(), which is why the hint asks for a distinct word per item.
public class SettingsItemAliasViewModel : ViewModelBase
{
    private readonly UserSettings _userSettings;

    public SettingsItemAliasViewModel(UserSettings userSettings)
    {
        _userSettings = userSettings;

        foreach (var entry in userSettings.SettingsItemAliases)
            Items.Add(new SettingsItemAliasItem(entry.Key, entry.Value));

        AddCommand = new RelayCommand(() => Items.Add(new SettingsItemAliasItem(string.Empty, string.Empty)));
        RemoveCommand = new RelayCommand<SettingsItemAliasItem>(item =>
        {
            if (item != null) Items.Remove(item);
        });
    }

    public ObservableCollection<SettingsItemAliasItem> Items { get; } = new();

    public ICommand AddCommand { get; }
    public ICommand RemoveCommand { get; }

    // A half-typed row is discarded rather than saved: an alias with no target can never hit, and a
    // target with no alias would sit in the file doing nothing.
    public void Save()
    {
        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in Items)
        {
            var alias = item.Alias.Trim();
            var target = item.Target.Trim();
            if (alias.Length == 0 || target.Length == 0) continue;
            aliases.TryAdd(alias, target);
        }

        _userSettings.SettingsItemAliases = aliases;
    }
}

public class SettingsItemAliasItem : ViewModelBase
{
    private string _alias;
    private string _target;

    public SettingsItemAliasItem(string alias, string target)
    {
        _alias = alias;
        _target = target;
    }

    public string Alias
    {
        get => _alias;
        set => SetProperty(ref _alias, value);
    }

    // The result's own display name, matched exactly. Windows names these items, so what to type here
    // is whatever the row shows in the search results.
    public string Target
    {
        get => _target;
        set => SetProperty(ref _target, value);
    }
}
