using System.Collections.ObjectModel;
using System.Windows.Input;
using Lertaro.App.Helpers;
using Lertaro.App.ViewModels.Search;
using Lertaro.Core;

namespace Lertaro.App.ViewModels.Settings;

public sealed class QuickLaunchSettingsViewModel : ViewModelBase
{
    private readonly UserSettings _userSettings;
    private string _newName = string.Empty;
    private string _newPath = string.Empty;
    private bool _isEnabled;

    public QuickLaunchSettingsViewModel(UserSettings userSettings)
    {
        _userSettings = userSettings;
        _isEnabled = userSettings.QuickLaunch.Enabled;
        SelectSectionCommand = new RelayCommand<string>(section => SelectedSection = section ?? "Items");
        foreach (var item in userSettings.QuickLaunch.Items)
            Items.Add(new QuickLaunchItemViewModel { Name = item.Name, Path = item.Path });
        Items.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasItems));

        var disabledIds = userSettings.QuickLaunch.DisabledSourceIds;
        var sourceOptions = QuickLaunchSourceCatalog.Providers
            .Select(provider =>
            {
                var id = QuickLaunchSourceCatalog.GetId(provider);
                return new QuickLaunchSourceOptionViewModel(id, provider.Name,
                    !disabledIds.Contains(id, StringComparer.OrdinalIgnoreCase));
            })
            .ToList();
        var sourceOptionsById = sourceOptions.ToDictionary(source => source.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var id in QuickLaunchSourceCatalog.OrderSourceIds(
                     sourceOptions.Select(source => source.Id), userSettings.QuickLaunch.SourceOrder))
        {
            Sources.Add(sourceOptionsById[id]);
        }

        AddCommand = new RelayCommand(Add, CanAdd);
        ClearCommand = new RelayCommand(Clear);
        RemoveCommand = new RelayCommand<QuickLaunchItemViewModel>(item => Items.Remove(item));
        EditCommand = new RelayCommand<QuickLaunchItemViewModel>(Edit);
        SaveEditCommand = new RelayCommand<QuickLaunchItemViewModel>(SaveEdit, CanSaveEdit);
        CancelEditCommand = new RelayCommand<QuickLaunchItemViewModel>(CancelEdit);
        MoveUpCommand = new RelayCommand<QuickLaunchItemViewModel>(item => Move(item, -1));
        MoveDownCommand = new RelayCommand<QuickLaunchItemViewModel>(item => Move(item, 1));
        BrowseFolderCommand = new RelayCommand(() => Browse(true));
        BrowseFileCommand = new RelayCommand(() => Browse(false));
        BrowseEditFolderCommand = new RelayCommand<QuickLaunchItemViewModel>(item => BrowseEdit(item, true));
        BrowseEditFileCommand = new RelayCommand<QuickLaunchItemViewModel>(item => BrowseEdit(item, false));
    }

    public ObservableCollection<QuickLaunchItemViewModel> Items { get; } = new();
    public ObservableCollection<QuickLaunchSourceOptionViewModel> Sources { get; } = new();
    public ICommand SelectSectionCommand { get; }
    public ICommand AddCommand { get; }
    public ICommand ClearCommand { get; }
    public ICommand RemoveCommand { get; }
    public ICommand EditCommand { get; }
    public ICommand SaveEditCommand { get; }
    public ICommand CancelEditCommand { get; }
    public ICommand MoveUpCommand { get; }
    public ICommand MoveDownCommand { get; }
    public ICommand BrowseFolderCommand { get; }
    public ICommand BrowseFileCommand { get; }
    public ICommand BrowseEditFolderCommand { get; }
    public ICommand BrowseEditFileCommand { get; }

    public bool HasItems => Items.Count > 0;

    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetProperty(ref _isEnabled, value);
    }

    private string _selectedSection = "Items";
    public string SelectedSection
    {
        get => _selectedSection;
        set => SetProperty(ref _selectedSection, value);
    }

    public string NewName
    {
        get => _newName;
        set => SetProperty(ref _newName, value);
    }

    public string NewPath
    {
        get => _newPath;
        set
        {
            if (!SetProperty(ref _newPath, value)) return;
            CommandManager.InvalidateRequerySuggested();
            if (string.IsNullOrWhiteSpace(NewName) && !FavoriteUrlHelper.IsWebUrl(value))
                NewName = LaunchItemNameHelper.GetAutomaticName(value);
        }
    }

    public void NotifyLanguageChanged()
    {
        foreach (var source in Sources)
        {
            if (QuickLaunchSourceCatalog.Find(source.Id) is { } provider)
                source.Name = provider.Name;
        }
    }

    public void Save()
    {
        var settings = _userSettings.QuickLaunch;
        settings.Enabled = IsEnabled;
        settings.Items = Items.Select(item => new QuickLaunchItemSetting { Name = item.Name, Path = item.Path }).ToList();
        var visibleIds = Sources.Select(source => source.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        settings.DisabledSourceIds = settings.DisabledSourceIds
            .Where(id => !visibleIds.Contains(id))
            .Concat(Sources.Where(source => !source.IsEnabled).Select(source => source.Id))
            .ToList();
        var sourceIds = Sources.Select(source => source.Id).ToList();
        var currentSourceIds = sourceIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        settings.SourceOrder = sourceIds
            .Concat(settings.SourceOrder.Where(id => !currentSourceIds.Contains(id)))
            .ToList();
    }

    private bool CanAdd() => FavoritePathResolver.IsPathAvailable(NewPath);

    private void Clear() => Items.Clear();

    private void Add()
    {
        if (!CanAdd()) return;
        Items.Add(new QuickLaunchItemViewModel { Name = NewName.Trim(), Path = NewPath.Trim().Trim('"') });
        NewName = string.Empty;
        NewPath = string.Empty;
    }

    private void Edit(QuickLaunchItemViewModel? item)
    {
        if (item == null) return;
        foreach (var other in Items.Where(other => other != item))
            other.IsEditing = false;
        item.EditName = item.Name;
        item.EditPath = item.Path;
        item.IsEditing = true;
    }

    private bool CanSaveEdit(QuickLaunchItemViewModel? item)
    {
        if (item == null) return false;
        var path = NormalizePath(item.EditPath);
        return FavoritePathResolver.IsPathAvailable(path)
            && !Items.Any(other => other != item
                && string.Equals(FavoritePathResolver.NormalizeForComparison(other.Path),
                    FavoritePathResolver.NormalizeForComparison(path), StringComparison.OrdinalIgnoreCase));
    }

    private void SaveEdit(QuickLaunchItemViewModel item)
    {
        if (!CanSaveEdit(item)) return;
        item.Name = string.IsNullOrWhiteSpace(item.EditName) ? string.Empty : item.EditName.Trim();
        item.Path = NormalizePath(item.EditPath);
        item.IsEditing = false;
    }

    private static void CancelEdit(QuickLaunchItemViewModel item) => item?.IsEditing = false;

    private void BrowseEdit(QuickLaunchItemViewModel item, bool folder)
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

    private void Move(QuickLaunchItemViewModel? item, int offset)
    {
        if (item == null) return;
        var index = Items.IndexOf(item);
        var target = index + offset;
        if (index >= 0 && target >= 0 && target < Items.Count) Items.Move(index, target);
    }

    private void Browse(bool folder)
    {
        if (folder)
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog { Multiselect = true };
            if (dialog.ShowDialog() == true) AddPaths(dialog.FolderNames);
        }
        else
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Multiselect = true };
            if (dialog.ShowDialog() == true) AddPaths(dialog.FileNames);
        }
    }

    internal void AddPaths(IEnumerable<string> paths)
    {
        var existing = Items
            .Select(item => FavoritePathResolver.NormalizeForComparison(item.Path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var rawPath in paths)
        {
            var path = rawPath.Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(path) || !FavoritePathResolver.IsPathAvailable(path))
                continue;
            if (!existing.Add(FavoritePathResolver.NormalizeForComparison(path)))
                continue;

            Items.Add(new QuickLaunchItemViewModel
            {
                Name = LaunchItemNameHelper.GetAutomaticName(path),
                Path = path
            });
        }

        NewName = string.Empty;
        NewPath = string.Empty;
    }
}

public sealed class QuickLaunchItemViewModel : ViewModelBase
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
    public string EditName { get => _editName; set => SetProperty(ref _editName, value); }
    public string EditPath
    {
        get => _editPath;
        set
        {
            if (SetProperty(ref _editPath, value))
                CommandManager.InvalidateRequerySuggested();
        }
    }
    public bool IsEditing { get => _isEditing; set => SetProperty(ref _isEditing, value); }
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? FavoritePathResolver.GetDisplayName(Path) : Name;
}

public sealed class QuickLaunchSourceOptionViewModel : ViewModelBase
{
    private string _name;
    private bool _isEnabled;
    public QuickLaunchSourceOptionViewModel(string id, string name, bool isEnabled)
    {
        Id = id;
        _name = name;
        _isEnabled = isEnabled;
    }
    public string Id { get; }
    public string Name { get => _name; set => SetProperty(ref _name, value); }
    public bool IsEnabled { get => _isEnabled; set => SetProperty(ref _isEnabled, value); }
}
