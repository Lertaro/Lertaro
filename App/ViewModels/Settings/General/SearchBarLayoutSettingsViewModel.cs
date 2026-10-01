using System.Windows.Input;
using Lertaro.App.Helpers;
using Lertaro.App.Services;
using Lertaro.Core;

namespace Lertaro.App.ViewModels.Settings.General;

// Edits stage locally and only commit to _userSettings/UiMetrics when Save() runs (called from
// GeneralSettingsViewModel.Apply()) -- see GeneralSettingsViewModel's class-level comment.
public class SearchBarLayoutSettingsViewModel : ViewModelBase
{
    private readonly UserSettings _userSettings;
    private double _searchBarWidth;
    private double _searchBarHeight;
    private bool _showClock;
    private bool _reopenAsFullWindowOnRepeatHotkey;
    private bool _lockPosition;
    // Reset() clears the quick window's remembered screen position -- there's no bound field for it
    // (the window itself owns Left/Top), so this just stages the intent for Save() to commit.
    private bool _resetPosition;

    public SearchBarLayoutSettingsViewModel(UserSettings userSettings)
    {
        _userSettings = userSettings;
        _searchBarWidth = userSettings.SearchWindow.SearchBarWidth;
        _searchBarHeight = userSettings.SearchWindow.SearchBarHeight;
        _showClock = userSettings.SearchWindow.ShowClock;
        _reopenAsFullWindowOnRepeatHotkey = userSettings.SearchWindow.ReopenAsFullWindowOnRepeatHotkey;
        _lockPosition = userSettings.SearchWindow.LockPosition;
    }

    public double SearchBarWidth
    {
        get => _searchBarWidth;
        set
        {
            if (value < 300.0 || value > 1200.0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Width must be between 300 and 1200.");
            }
            SetProperty(ref _searchBarWidth, value);
        }
    }

    public double SearchBarHeight
    {
        get => _searchBarHeight;
        set
        {
            if (value < 45.0 || value > 120.0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Height must be between 45 and 120.");
            }
            SetProperty(ref _searchBarHeight, value);
        }
    }

    public bool ShowClock
    {
        get => _showClock;
        set => SetProperty(ref _showClock, value);
    }

    public bool ReopenAsFullWindowOnRepeatHotkey
    {
        get => _reopenAsFullWindowOnRepeatHotkey;
        set => SetProperty(ref _reopenAsFullWindowOnRepeatHotkey, value);
    }

    public bool LockPosition
    {
        get => _lockPosition;
        set => SetProperty(ref _lockPosition, value);
    }

    public ICommand ResetCommand => new RelayCommand(Reset);

    private void Reset()
    {
        SearchBarWidth = 570;
        SearchBarHeight = 60;
        ShowClock = false;
        ReopenAsFullWindowOnRepeatHotkey = false;
        // Unlocked as well as re-centred: Reset exists to undo a layout you no longer want, and leaving
        // the lock on would hand back a window that cannot be moved off wherever it lands.
        LockPosition = false;
        _resetPosition = true;
    }

    public void Save()
    {
        _userSettings.SearchWindow.SearchBarWidth = _searchBarWidth;
        _userSettings.SearchWindow.SearchBarHeight = _searchBarHeight;
        _userSettings.SearchWindow.ShowClock = _showClock;
        _userSettings.SearchWindow.ReopenAsFullWindowOnRepeatHotkey = _reopenAsFullWindowOnRepeatHotkey;
        _userSettings.SearchWindow.LockPosition = _lockPosition;
        if (_resetPosition)
        {
            _userSettings.SearchWindow.RelativeLeft = null;
            _userSettings.SearchWindow.RelativeTop = null;
            _resetPosition = false;
        }
        UiMetrics.ApplyScaleFromSettings();
    }
}
