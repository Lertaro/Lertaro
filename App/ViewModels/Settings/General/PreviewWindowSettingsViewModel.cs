using System.Windows.Input;
using Lertaro.App.Helpers;
using Lertaro.App.Services;
using Lertaro.Core;

namespace Lertaro.App.ViewModels.Settings.General;

// Edits stage locally and only commit to _userSettings/UiMetrics when Save() runs (called from
// GeneralSettingsViewModel.Apply()) -- see GeneralSettingsViewModel's class-level comment.
public class PreviewWindowSettingsViewModel : ViewModelBase
{
    private readonly UserSettings _userSettings;
    private double _width;
    private double _height;

    public PreviewWindowSettingsViewModel(UserSettings userSettings)
    {
        _userSettings = userSettings;
        _width = userSettings.PreviewWindow.Width;
        _height = userSettings.PreviewWindow.Height;
    }

    public double Width
    {
        get => _width;
        set
        {
            if (value < UiMetrics.MinPreviewWindowWidth || value > UiMetrics.MaxPreviewWindowWidth)
            {
                throw new ArgumentOutOfRangeException(nameof(value),
                    $"Width must be between {UiMetrics.MinPreviewWindowWidth} and {UiMetrics.MaxPreviewWindowWidth}.");
            }
            SetProperty(ref _width, value);
        }
    }

    public double Height
    {
        get => _height;
        set
        {
            if (value < UiMetrics.MinPreviewWindowHeight || value > UiMetrics.MaxPreviewWindowHeight)
            {
                throw new ArgumentOutOfRangeException(nameof(value),
                    $"Height must be between {UiMetrics.MinPreviewWindowHeight} and {UiMetrics.MaxPreviewWindowHeight}.");
            }
            SetProperty(ref _height, value);
        }
    }

    public ICommand ResetCommand => new RelayCommand(Reset);

    private void Reset()
    {
        Width = 400;
        Height = 529;
    }

    public void Save()
    {
        _userSettings.PreviewWindow.Width = _width;
        _userSettings.PreviewWindow.Height = _height;
        UiMetrics.ApplyScaleFromSettings();
    }
}
