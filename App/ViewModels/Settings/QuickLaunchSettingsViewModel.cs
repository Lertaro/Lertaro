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
        foreach (var item in userSettings.QuickLaunch.Items)
            Items.Add(new QuickLaunchItemViewModel { Name = item.Name, Path = item.Path });

        var selectedIds = QuickLaunchSourceCatalog.GetEffectiveSourceIds(userSettings.QuickLaunch);
        foreach (var provider in QuickLaunchSourceCatalog.Providers)
        {
            var id = QuickLaunchSourceCatalog.GetId(provider);
            Sources.Add(new QuickLaunchSourceOptionViewModel(id, provider.Name, selectedIds.Contains(id, StringComparer.OrdinalIgnoreCase)));
        }

        AddCommand = new RelayCommand(Add, CanAdd);
        RemoveCommand = new RelayCommand<QuickLaunchItemViewModel>(item => Items.Remove(item));
        EditCommand = new RelayCommand<QuickLaunchItemViewModel>(Edit);
        MoveUpCommand = new RelayCommand<QuickLaunchItemViewModel>(item => Move(item, -1));
        MoveDownCommand = new RelayCommand<QuickLaunchItemViewModel>(item => Move(item, 1));
        BrowseFolderCommand = new RelayCommand(() => Browse(true));
        BrowseFileCommand = new RelayCommand(() => Browse(false));
    }

    public ObservableCollection<QuickLaunchItemViewModel> Items { get; } = new();
    public ObservableCollection<QuickLaunchSourceOptionViewModel> Sources { get; } = new();
    public ICommand AddCommand { get; }
    public ICommand RemoveCommand { get; }
    public ICommand EditCommand { get; }
    public ICommand MoveUpCommand { get; }
    public ICommand MoveDownCommand { get; }
    public ICommand BrowseFolderCommand { get; }
    public ICommand BrowseFileCommand { get; }

    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetProperty(ref _isEnabled, value);
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
                NewName = FavoritePathResolver.GetDisplayName(value);
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
        settings.EnabledSourceIds = settings.EnabledSourceIds
            .Where(id => !visibleIds.Contains(id))
            .Concat(Sources.Where(source => source.IsEnabled).Select(source => source.Id))
            .ToList();
        settings.SourceSelectionInitialized = true;
    }

    private bool CanAdd() => FavoritePathResolver.IsPathAvailable(NewPath);

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
        NewName = item.Name;
        NewPath = item.Path;
        Items.Remove(item);
    }

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
            var dialog = new Microsoft.Win32.OpenFolderDialog();
            if (dialog.ShowDialog() == true) NewPath = dialog.FolderName;
        }
        else
        {
            var dialog = new Microsoft.Win32.OpenFileDialog();
            if (dialog.ShowDialog() == true) NewPath = dialog.FileName;
        }
    }
}

public sealed class QuickLaunchItemViewModel : ViewModelBase
{
    private string _name = string.Empty;
    private string _path = string.Empty;
    public string Name { get => _name; set => SetProperty(ref _name, value); }
    public string Path { get => _path; set => SetProperty(ref _path, value); }
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
