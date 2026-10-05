using System.Collections.ObjectModel;
using System.Windows.Input;
using Lertaro.App.Helpers;
using Lertaro.Core;

namespace Lertaro.App.ViewModels.Settings.General;

// Edits the alias -> item-name table SearchableItemMapper reads when a query is exactly one of the words
// the user registered. Rows stage here and only reach UserSettings.SettingsItemAliases when Save() runs
// (called from GeneralSettingsApplier), same as the order lists beside this one.
// A word can appear on several rows, one for each target it should surface.
public class SettingsItemAliasViewModel : ViewModelBase
{
    private readonly UserSettings _userSettings;

    public SettingsItemAliasViewModel(UserSettings userSettings)
    {
        _userSettings = userSettings;

        foreach (var entry in userSettings.SettingsItemAliases)
            foreach (var target in entry.Value)
                Items.Add(new SettingsItemAliasItem(entry.Key, target));

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
        var aliases = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in Items)
        {
            var alias = item.Alias.Trim();
            var target = item.Target.Trim();
            if (alias.Length == 0 || target.Length == 0) continue;
            if (!aliases.TryGetValue(alias, out var targets)) aliases[alias] = targets = [];
            if (!targets.Contains(target, StringComparer.OrdinalIgnoreCase)) targets.Add(target);
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

    // Exact display name for compatibility, or a provider's stable item ID.
    public string Target
    {
        get => _target;
        set => SetProperty(ref _target, value);
    }
}
