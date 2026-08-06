using System.Windows.Input;
using Lertaro.App.Helpers;
using Lertaro.App.Services;
using Lertaro.Core;

namespace Lertaro.App.ViewModels.Settings.General;

// Settings for the full/main SearchWindow's default size -- distinct from SearchWindowSettings,
// which configures the quick window's search bar layout. Edits stage locally and only commit to
// _userSettings/UiMetrics when Save() runs (called from GeneralSettingsViewModel.Apply(), i.e. the
// Settings window's Apply/OK button) -- see GeneralSettingsViewModel's class-level comment.
public class MainWindowSettingsViewModel : ViewModelBase
{
    private readonly UserSettings _userSettings;
    private double _width;
    private double _height;

    public MainWindowSettingsViewModel(UserSettings userSettings)
    {
        _userSettings = userSettings;
        _width = userSettings.MainWindow.Width;
        _height = userSettings.MainWindow.Height;
    }

    public double Width
    {
        get => _width;
        set
        {
            if (value < UiMetrics.MinMainWindowWidth || value > UiMetrics.MaxMainWindowWidth)
            {
                throw new ArgumentOutOfRangeException(nameof(value),
                    $"Width must be between {UiMetrics.MinMainWindowWidth} and {UiMetrics.MaxMainWindowWidth}.");
            }
            SetProperty(ref _width, value);
        }
    }

    public double Height
    {
        get => _height;
        set
        {
            if (value < UiMetrics.MinMainWindowHeight || value > UiMetrics.MaxMainWindowHeight)
            {
                throw new ArgumentOutOfRangeException(nameof(value),
                    $"Height must be between {UiMetrics.MinMainWindowHeight} and {UiMetrics.MaxMainWindowHeight}.");
            }
            SetProperty(ref _height, value);
        }
    }

    public ICommand ResetCommand => new RelayCommand(Reset);

    private void Reset()
    {
        Width = UiMetrics.DefaultMainWindowWidth;
        Height = UiMetrics.DefaultMainWindowHeight;
    }

    public void Save()
    {
        _userSettings.MainWindow.Width = _width;
        _userSettings.MainWindow.Height = _height;
        UiMetrics.ApplyScaleFromSettings();
    }
}
