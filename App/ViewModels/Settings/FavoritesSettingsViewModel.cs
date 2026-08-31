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
        Items.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasItems));

        AddCommand = new RelayCommand(Add, CanAdd);
        ClearCommand = new RelayCommand(Clear);
        RemoveCommand = new RelayCommand<FavoriteItemViewModel>(Remove);
        EditCommand = new RelayCommand<FavoriteItemViewModel>(Edit);
        SaveEditCommand = new RelayCommand<FavoriteItemViewModel>(SaveEdit, CanSaveEdit);
        CancelEditCommand = new RelayCommand<FavoriteItemViewModel>(CancelEdit);
        BrowseFolderCommand = new RelayCommand(BrowseFolder);
        BrowseFileCommand = new RelayCommand(BrowseFile);
        BrowseEditFolderCommand = new RelayCommand<FavoriteItemViewModel>(item => BrowseEdit(item, true));
        BrowseEditFileCommand = new RelayCommand<FavoriteItemViewModel>(item => BrowseEdit(item, false));
        MoveUpCommand = new RelayCommand<FavoriteItemViewModel>(MoveUp);
        MoveDownCommand = new RelayCommand<FavoriteItemViewModel>(MoveDown);
        AddPathPresetCommand = new RelayCommand<string>(AddPathPreset);
    }

    public ObservableCollection<FavoriteItemViewModel> Items { get; } = new();

    public ICommand AddCommand { get; }
    public ICommand ClearCommand { get; }
    public ICommand RemoveCommand { get; }
    public ICommand EditCommand { get; }
    public ICommand SaveEditCommand { get; }
    public ICommand CancelEditCommand { get; }
    public ICommand BrowseFolderCommand { get; }
    public ICommand BrowseFileCommand { get; }
    public ICommand BrowseEditFolderCommand { get; }
    public ICommand BrowseEditFileCommand { get; }
    public ICommand MoveUpCommand { get; }
    public ICommand MoveDownCommand { get; }
    public ICommand AddPathPresetCommand { get; }

    public bool HasItems => Items.Count > 0;

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
                // A URL leaves the name blank so the full URL shows; a real file path auto-fills
                // from its file name, and a shell virtual path auto-fills from the shell display name.
                if (string.IsNullOrWhiteSpace(NewName) && !string.IsNullOrWhiteSpace(value)
                    && !FavoriteUrlHelper.IsWebUrl(value))
                {
                    try
                    {
                        var expanded = FavoritePathResolver.Expand(value);
                        if (FavoritePathResolver.IsVirtualPath(expanded))
                        {
                            var virtualName = PluginSdk.Helpers.ShellPathHelper.GetVirtualFolderDisplayName(expanded, string.Empty);
                            if (!string.IsNullOrEmpty(virtualName))
                                NewName = virtualName;
                        }
                        else
                        {
                            var name = System.IO.Path.GetFileName(expanded.TrimEnd('\\', '/'));
                            if (!string.IsNullOrEmpty(name))
                                NewName = name;
                        }
                    }
                    catch { }
                }
            }
        }
    }

    private bool CanAdd() => FavoritePathResolver.IsPathAvailable(NewPath);

    private void Clear() => Items.Clear();

    private void Add()
    {
        var name = string.IsNullOrWhiteSpace(NewName) ? string.Empty : NewName.Trim();
        var path = NewPath.Trim().Trim('"');

        if (string.IsNullOrWhiteSpace(path)) return;

        Items.Add(new FavoriteItemViewModel { Name = name, Path = path });
        NewName = string.Empty;
        NewPath = string.Empty;
    }

    public void AddPathPreset(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        NewName = string.Empty;
        NewPath = path;
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
        if (item == null) return;
        foreach (var other in Items.Where(other => other != item))
            other.IsEditing = false;
        item.EditName = item.Name;
        item.EditPath = item.Path;
        item.IsEditing = true;
    }

    private bool CanSaveEdit(FavoriteItemViewModel? item)
    {
        if (item == null) return false;
        var path = NormalizePath(item.EditPath);
        return FavoritePathResolver.IsPathAvailable(path)
            && !Items.Any(other => other != item
                && string.Equals(FavoritePathResolver.NormalizeForComparison(other.Path),
                    FavoritePathResolver.NormalizeForComparison(path), StringComparison.OrdinalIgnoreCase));
    }

    private void SaveEdit(FavoriteItemViewModel item)
    {
        if (!CanSaveEdit(item)) return;
        item.Name = string.IsNullOrWhiteSpace(item.EditName) ? string.Empty : item.EditName.Trim();
        item.Path = NormalizePath(item.EditPath);
        item.IsEditing = false;
    }

    private static void CancelEdit(FavoriteItemViewModel item) => item?.IsEditing = false;

    private void BrowseEdit(FavoriteItemViewModel item, bool folder)
    {
        if (item == null) return;
        if (folder)
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog();
            if (dialog.ShowDialog() == true) item.EditPath = dialog.FolderName;
        }
        else
        {
            var dialog = new Microsoft.Win32.OpenFileDialog();
            if (dialog.ShowDialog() == true) item.EditPath = dialog.FileName;
        }
    }

    private static string NormalizePath(string value) => value.Trim().Trim('"');

    private void BrowseFolder()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Multiselect = true };
        if (dialog.ShowDialog() == true) AddPaths(dialog.FolderNames);
    }

    private void BrowseFile()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Multiselect = true };
        if (dialog.ShowDialog() == true) AddPaths(dialog.FileNames);
    }

    internal void AddPaths(IEnumerable<string> paths) => FavoritesSettingsPathSupport.AddPaths(this, paths);

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
    private string _editName = string.Empty;
    private string _editPath = string.Empty;
    private bool _isEditing;

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

    public string EditName
    {
        get => _editName;
        set => SetProperty(ref _editName, value);
    }

    public string EditPath
    {
        get => _editPath;
        set
        {
            if (SetProperty(ref _editPath, value))
                CommandManager.InvalidateRequerySuggested();
        }
    }

    public bool IsEditing
    {
        get => _isEditing;
        set => SetProperty(ref _isEditing, value);
    }

    public string DisplayName
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Name))
                return Name;

            var expanded = FavoritePathResolver.Expand(Path);
            if (FavoritePathResolver.IsVirtualPath(expanded))
            {
                return PluginSdk.Helpers.ShellPathHelper.GetVirtualFolderDisplayName(expanded, Path);
            }
            if (FavoriteUrlHelper.IsWebUrl(Path))
            {
                return Path.Trim();
            }
            try
            {
                var name = System.IO.Path.GetFileName(expanded.TrimEnd('\\', '/'));
                return string.IsNullOrEmpty(name) ? Path : name;
            }
            catch { return Path; }
        }
    }
}
