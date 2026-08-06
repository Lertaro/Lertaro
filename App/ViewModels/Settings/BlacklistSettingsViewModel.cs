using Lertaro.Core;

namespace Lertaro.App.ViewModels.Settings;

/// <summary>
/// The Hotkeys page's blacklist tab: the global list of processes the keyboard hook stands down for,
/// and the quick panel's own additions to it. Both are the same editor
/// (<see cref="ProcessBlacklistEditorViewModel"/>) -- this type only says which two lists there are and
/// where they are stored.
/// </summary>
public class BlacklistSettingsViewModel : ViewModelBase
{
    private readonly UserSettings _userSettings;

    public BlacklistSettingsViewModel(UserSettings userSettings)
    {
        _userSettings = userSettings;
        Global = new ProcessBlacklistEditorViewModel(userSettings.BlacklistedProcesses);
        QuickPanel = new ProcessBlacklistEditorViewModel(userSettings.QuickPanel.BlacklistedProcesses);
    }

    /// <summary>Applies to every global hotkey and to inline search -- see ForegroundProcessGate.</summary>
    public ProcessBlacklistEditorViewModel Global { get; }

    /// <summary>
    /// Applies to the quick panel only, ON TOP of <see cref="Global"/> rather than instead of it: the
    /// panel refuses to open over anything on either list.
    /// </summary>
    public ProcessBlacklistEditorViewModel QuickPanel { get; }

    public void Save()
    {
        _userSettings.BlacklistedProcesses = Global.ToSettingsList();
        _userSettings.QuickPanel.BlacklistedProcesses = QuickPanel.ToSettingsList();
    }
}

public sealed class BlacklistProcessItem
{
    public BlacklistProcessItem(string value) => Value = value;

    public string Value { get; }
}
