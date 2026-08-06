using System.Collections.ObjectModel;
using System.Windows.Input;
using Lertaro.App.Helpers;
using Lertaro.Core;

namespace Lertaro.App.ViewModels.Settings;

public class FavoritesSettingsViewModel : ViewModelBase
{
    private readonly UserSettings _userSettings;
    private string _newName = string.Empty;
    private string _newPath = string.Empty;

    public FavoritesSettingsViewModel(UserSettings userSettings)
    {
        _userSettings = userSettings;
        foreach (var fav in _userSettings.Favorites)
        {
            Items.Add(new FavoriteItemViewModel { Name = fav.Name, Path = fav.Path });
        }

        AddCommand = new RelayCommand(Add, CanAdd);
        RemoveCommand = new RelayCommand<FavoriteItemViewModel>(Remove);
        EditCommand = new RelayCommand<FavoriteItemViewModel>(Edit);
        BrowseFolderCommand = new RelayCommand(BrowseFolder);
        BrowseFileCommand = new RelayCommand(BrowseFile);
        MoveUpCommand = new RelayCommand<FavoriteItemViewModel>(MoveUp);
        MoveDownCommand = new RelayCommand<FavoriteItemViewModel>(MoveDown);
    }

    public ObservableCollection<FavoriteItemViewModel> Items { get; } = new();

    public ICommand AddCommand { get; }
    public ICommand RemoveCommand { get; }
    public ICommand EditCommand { get; }
    public ICommand BrowseFolderCommand { get; }
    public ICommand BrowseFileCommand { get; }
    public ICommand MoveUpCommand { get; }
    public ICommand MoveDownCommand { get; }

    public string NewName
    {
        get => _newName;
        set
        {
            if (SetProperty(ref _newName, value))
                CommandManager.InvalidateRequerySuggested();
        }
    }

    public string NewPath
    {
        get => _newPath;
        set
        {
            if (SetProperty(ref _newPath, value))
            {
                CommandManager.InvalidateRequerySuggested();
                // Don't auto-fill a name from a URL (GetFileName would give a stray last segment); leave it
                // blank so the full URL shows as the display name.
                if (string.IsNullOrWhiteSpace(NewName) && !string.IsNullOrWhiteSpace(value) && !FavoriteUrlHelper.IsWebUrl(value))
                {
                    try
                    {
                        var name = System.IO.Path.GetFileName(value.TrimEnd('\\', '/'));
                        if (!string.IsNullOrEmpty(name))
                            NewName = name;
                    }
                    catch { }
                }
            }
        }
    }

    private bool CanAdd() => !string.IsNullOrWhiteSpace(NewPath);

    private void Add()
    {
        var name = string.IsNullOrWhiteSpace(NewName) ? string.Empty : NewName.Trim();
        var path = NewPath.Trim().Trim('"');

        if (string.IsNullOrWhiteSpace(path)) return;

        Items.Add(new FavoriteItemViewModel { Name = name, Path = path });
        NewName = string.Empty;
        NewPath = string.Empty;
    }

    private void Remove(FavoriteItemViewModel item)
    {
        if (item != null)
        {
            Items.Remove(item);
        }
    }

    private void Edit(FavoriteItemViewModel item)
    {
        if (item != null)
        {
            NewName = string.IsNullOrWhiteSpace(item.Name) ? item.DisplayName : item.Name;
            NewPath = item.Path;
            Items.Remove(item);
        }
    }

    private void BrowseFolder()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog();
        if (dialog.ShowDialog() == true)
        {
            NewPath = dialog.FolderName;
        }
    }

    private void BrowseFile()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog();
        if (dialog.ShowDialog() == true)
        {
            NewPath = dialog.FileName;
        }
    }

    private void MoveUp(FavoriteItemViewModel item)
    {
        if (item == null) return;
        var idx = Items.IndexOf(item);
        if (idx > 0)
        {
            Items.Move(idx, idx - 1);
        }
    }

    private void MoveDown(FavoriteItemViewModel item)
    {
        if (item == null) return;
        var idx = Items.IndexOf(item);
        if (idx >= 0 && idx < Items.Count - 1)
        {
            Items.Move(idx, idx + 1);
        }
    }

    public void Save()
    {
        _userSettings.Favorites = Items.Select(x => new FavoriteItemSetting { Name = x.Name, Path = x.Path }).ToList();
        _userSettings.Save();
    }
}

public class FavoriteItemViewModel : ViewModelBase
{
    private string _name = string.Empty;
    private string _path = string.Empty;

    public string Name
    {
        get => _name;
        set
        {
            if (SetProperty(ref _name, value))
                OnPropertyChanged(nameof(DisplayName));
        }
    }

    public string Path
    {
        get => _path;
        set
        {
            if (SetProperty(ref _path, value))
                OnPropertyChanged(nameof(DisplayName));
        }
    }

    public string DisplayName
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Name))
                return Name;

            if (Path.StartsWith("shell:::", StringComparison.OrdinalIgnoreCase) || Path.StartsWith("::", StringComparison.OrdinalIgnoreCase))
            {
                return PluginSdk.Helpers.ShellPathHelper.GetVirtualFolderDisplayName(Path, Path);
            }
            if (FavoriteUrlHelper.IsWebUrl(Path))
            {
                return Path.Trim();
            }
            try
            {
                var name = System.IO.Path.GetFileName(Path.TrimEnd('\\', '/'));
                return string.IsNullOrEmpty(name) ? Path : name;
            }
            catch { return Path; }
        }
    }
}
