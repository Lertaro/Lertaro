using System.Windows.Input;
using Lertaro.App.Helpers;
using Lertaro.App.Services;
using Lertaro.Core;

namespace Lertaro.App.ViewModels.Settings.General;

// Split out of GeneralSettingsViewModel to keep that file under the repo's per-file line limit; the
// full-window geometry, log-level and language concerns stay there, while this owns the one cohesive
// "redirect folder opening to a third-party file manager" feature (GitHub issue #180).
//
// Same staging convention as its former host: edits land locally and only reach _userSettings when
// Save() runs (from GeneralSettingsViewModel.Apply(), i.e. the Settings window's Apply/OK button).
public class DefaultFileManagerSettingsViewModel : ViewModelBase
{
    private readonly UserSettings _userSettings;
    private bool _enabled;
    private string _path;
    private string _parameter;

    public DefaultFileManagerSettingsViewModel(UserSettings userSettings)
    {
        _userSettings = userSettings;
        _enabled = userSettings.DefaultFileManager.Enabled;
        _path = userSettings.DefaultFileManager.Path;
        _parameter = userSettings.DefaultFileManager.Parameter;
    }

    public bool Enabled
    {
        get => _enabled;
        set => SetProperty(ref _enabled, value);
    }

    public string Path
    {
        get => _path;
        set => SetProperty(ref _path, value);
    }

    public string Parameter
    {
        get => _parameter;
        set => SetProperty(ref _parameter, value);
    }

    private ICommand? _browsePathCommand;
    public ICommand BrowsePathCommand => _browsePathCommand ??= new RelayCommand(BrowsePath);

    private void BrowsePath()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = $"{TranslationManager.Instance["General_DefaultFileManagerBrowseFilter"]}|*.exe" };
        if (dialog.ShowDialog() == true)
            Path = dialog.FileName;
    }

    public void Save()
    {
        _userSettings.DefaultFileManager.Enabled = _enabled;
        _userSettings.DefaultFileManager.Path = _path;
        _userSettings.DefaultFileManager.Parameter = _parameter;
    }
}
