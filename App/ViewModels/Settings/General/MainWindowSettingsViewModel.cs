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
    private bool _singleInstance;
    private bool _closeOnRepeatHotkey;

    public MainWindowSettingsViewModel(UserSettings userSettings)
    {
        _userSettings = userSettings;
        _width = UiMetrics.RoundWindowSize(userSettings.MainWindow.Width);
        _height = UiMetrics.RoundWindowSize(userSettings.MainWindow.Height);
        _singleInstance = userSettings.MainWindow.SingleInstance;
        _closeOnRepeatHotkey = userSettings.MainWindow.CloseOnRepeatHotkey;
    }

    public double Width
    {
        get => _width;
        set
        {
            var rounded = UiMetrics.RoundWindowSize(value);
            if (rounded < UiMetrics.MinMainWindowWidth || rounded > UiMetrics.MaxMainWindowWidth)
            {
                throw new ArgumentOutOfRangeException(nameof(value),
                    $"Width must be between {UiMetrics.MinMainWindowWidth} and {UiMetrics.MaxMainWindowWidth}.");
            }
            SetProperty(ref _width, rounded);
        }
    }

    public double Height
    {
        get => _height;
        set
        {
            var rounded = UiMetrics.RoundWindowSize(value);
            if (rounded < UiMetrics.MinMainWindowHeight || rounded > UiMetrics.MaxMainWindowHeight)
            {
                throw new ArgumentOutOfRangeException(nameof(value),
                    $"Height must be between {UiMetrics.MinMainWindowHeight} and {UiMetrics.MaxMainWindowHeight}.");
            }
            SetProperty(ref _height, rounded);
        }
    }

    public bool SingleInstance
    {
        get => _singleInstance;
        set => SetProperty(ref _singleInstance, value);
    }

    // Not folded into Reset: that command is about the window's geometry ("Reset Search Window
    // Settings"), and silently flipping a behaviour a user deliberately opted into would be a surprise.
    public bool CloseOnRepeatHotkey
    {
        get => _closeOnRepeatHotkey;
        set => SetProperty(ref _closeOnRepeatHotkey, value);
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
        _userSettings.MainWindow.SingleInstance = _singleInstance;
        _userSettings.MainWindow.CloseOnRepeatHotkey = _closeOnRepeatHotkey;
        UiMetrics.ApplyScaleFromSettings();
    }
}
