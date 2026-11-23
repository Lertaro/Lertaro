using Lertaro.App.Helpers.Visuals;
using Lertaro.App.Services;
using Lertaro.Core;

namespace Lertaro.App.ViewModels.Settings.General;

// Stages one search window's display preference until its owning page saves.
public sealed class SearchDisplaySettingsViewModel : ViewModelBase
{
    private readonly UserSettings _userSettings;
    private readonly bool _isQuickWindow;
    private string _deviceName;

    public SearchDisplaySettingsViewModel(UserSettings userSettings, bool isQuickWindow)
    {
        _userSettings = userSettings;
        _isQuickWindow = isQuickWindow;
        _deviceName = (isQuickWindow ? userSettings.SearchWindow.DefaultDisplay : userSettings.MainWindow.DefaultDisplay) ?? string.Empty;
    }

    public string DeviceName
    {
        get => _deviceName;
        // WPF clears SelectedValue while replacing a translated ItemsSource. Empty, not null, is the
        // actual "follow mouse" option, so a transient null must not erase the staged selection.
        set { if (value != null) SetProperty(ref _deviceName, value); }
    }

    public IReadOnlyList<KeyValuePair<string, string>> Options
    {
        get
        {
            var translations = TranslationManager.Instance;
            var options = new List<KeyValuePair<string, string>>
            {
                new(string.Empty, translations["General_DisplayFollowMouse"])
            };
            foreach (var screen in Screen.AllScreens)
            {
                var displayName = screen.DeviceName.Replace(@"\\.\", string.Empty);
                options.Add(new(screen.DeviceName, screen.Primary
                    ? string.Format(translations["General_DisplayPrimary"], displayName) : displayName));
            }
            if (SearchWindowDisplay.ResolveDisplayIndex(_deviceName, options.Select(option => option.Key).ToArray()) < 0
                && !string.IsNullOrEmpty(_deviceName))
                options.Add(new(_deviceName, string.Format(translations["General_DisplayUnavailable"], _deviceName.Replace(@"\\.\", string.Empty))));
            return options;
        }
    }

    internal void NotifyLanguageChanged()
    {
        OnPropertyChanged(nameof(Options));
        OnPropertyChanged(nameof(DeviceName));
    }

    public void Save()
    {
        if (_isQuickWindow) _userSettings.SearchWindow.DefaultDisplay = _deviceName;
        else _userSettings.MainWindow.DefaultDisplay = _deviceName;
    }
}
