using System.Windows.Input;
using Lertaro.App.Helpers;
using Lertaro.App.Services;
using Lertaro.Core;
using Lertaro.Core.Wire;
namespace Lertaro.App.ViewModels.Settings;

public class HotkeySettingsViewModel : ViewModelBase
{
    private readonly System.ComponentModel.PropertyChangedEventHandler _translationHandler;

    private readonly UserSettings _userSettings;

    public HotkeySettingsViewModel(UserSettings userSettings, BlacklistSettingsViewModel blacklist)
    {
        _userSettings = userSettings;
        Blacklist = blacklist;
        var hotkeys = _userSettings.Hotkeys;

        // Initialize local bindings from user settings
        _toggleHotkeyValue = hotkeys.ToggleWindowHotkey;
        _allowHotkeysInFullscreen = hotkeys.AllowHotkeysInFullscreen;
        _openFullWindowByDefault = hotkeys.OpenFullWindowByDefault;
        _quickSwitchHotkeyValue = hotkeys.QuickSwitchHotkey;

        _quickNavTriggerOnDoubleClick = hotkeys.QuickNavTriggerOnDoubleClick;
        _quickNavTriggerOnMiddleClick = hotkeys.QuickNavTriggerOnMiddleClick;

        _selectJumpModifier = hotkeys.SelectJumpModifier;
        _nextItemHotkey = hotkeys.NextItemHotkey;
        _previousItemHotkey = hotkeys.PreviousItemHotkey;
        _actionsMenuHotkey = hotkeys.ActionsMenuHotkey;
        _completeFromSelectionHotkey = hotkeys.CompleteFromSelectionHotkey;
        _quickLookHotkey = hotkeys.QuickLookHotkey;
        _keywordHistoryPreviousHotkey = hotkeys.KeywordHistoryPreviousHotkey;
        _keywordHistoryNextHotkey = hotkeys.KeywordHistoryNextHotkey;
        _keywordHistoryDeleteHotkey = hotkeys.KeywordHistoryDeleteHotkey;
        _openFullWindowHotkey = hotkeys.OpenFullWindowHotkey;
        _localSendSendWindowHotkey = hotkeys.LocalSendSendWindowHotkey;
        _stayOpenHotkey = hotkeys.StayOpenHotkey;
        _quickPanelHotkey = hotkeys.QuickPanelHotkey;

        PluginActionGroups = HotkeyPluginActionGroupBuilder.Build(hotkeys.PluginActionHotkeys);

        // Plugin action DisplayName/plugin Name are read live off the action/plugin objects, so they
        // need an explicit refresh on a runtime language switch (nothing else re-raises them).
        _translationHandler = (s, e) =>
        {
            foreach (var group in PluginActionGroups)
            {
                group.RefreshPluginName();
                foreach (var item in group.Items) item.RefreshDisplayName();
            }
        };
        TranslationManager.Instance.PropertyChanged += _translationHandler;

    }

    // Tab navigation
    private string _selectedTab = "Global";
    public string SelectedTab
    {
        get => _selectedTab;
        set => SetProperty(ref _selectedTab, value);
    }

    private ICommand? _selectTabCommand;
    public ICommand SelectTabCommand => _selectTabCommand ??= new RelayCommand<string>(tab => SelectedTab = tab);

    // Process blacklist, nested here as a third tab (Global/PluginActions/Blacklist share this page now).
    public BlacklistSettingsViewModel Blacklist { get; }

    public List<PluginActionGroupViewModel> PluginActionGroups { get; }

    // Quick Navigation properties
    private bool _quickNavTriggerOnDoubleClick;
    public bool QuickNavTriggerOnDoubleClick
    {
        get => _quickNavTriggerOnDoubleClick;
        set => SetProperty(ref _quickNavTriggerOnDoubleClick, value);
    }

    private bool _quickNavTriggerOnMiddleClick;
    public bool QuickNavTriggerOnMiddleClick
    {
        get => _quickNavTriggerOnMiddleClick;
        set => SetProperty(ref _quickNavTriggerOnMiddleClick, value);
    }

    // Toggle Window hotkey: a single recorder value stored verbatim in the flat format described by
    // HotkeyStringFormat. A bare modifier (e.g. "Ctrl") means "double-tap this modifier"; a full combo
    // (e.g. "Alt+Space") means a literal key combination.
    private string _toggleHotkeyValue;
    public string ToggleHotkeyValue
    {
        get => _toggleHotkeyValue;
        set
        {
            if (SetProperty(ref _toggleHotkeyValue, value))
                OnPropertyChanged(nameof(IsToggleModifierClick));
        }
    }

    // Whether the toggle hotkey is currently a bare modifier (double-tap mode) -- drives the
    // "(Double Tap)" hint shown next to the recorder.
    public bool IsToggleModifierClick => HotkeyStringFormat.IsBareModifier(_toggleHotkeyValue, out _);

    // Opts every global hotkey (this one, Quick Switch, inline search) out of the automatic
    // fullscreen-app exemption -- see KeyboardHookService's shouldDisableAllHooks gate.
    private bool _allowHotkeysInFullscreen;
    public bool AllowHotkeysInFullscreen
    {
        get => _allowHotkeysInFullscreen;
        set => SetProperty(ref _allowHotkeysInFullscreen, value);
    }

    // When true, the toggle hotkey opens the full SearchWindow instead of the QuickSearchWindow.
    private bool _openFullWindowByDefault;
    public bool OpenFullWindowByDefault
    {
        get => _openFullWindowByDefault;
        set => SetProperty(ref _openFullWindowByDefault, value);
    }

    // Quick Switch: same flat format, bound directly to the recorder (combo-only in the current UI).
    private string _quickSwitchHotkeyValue;
    public string QuickSwitchComboHotkey
    {
        get => _quickSwitchHotkeyValue;
        set => SetProperty(ref _quickSwitchHotkeyValue, value);
    }

    // Function key shortcuts, each stored directly in HotkeyRecorderControl's own combo format
    private string _selectJumpModifier;
    public string SelectJumpModifier
    {
        get => _selectJumpModifier;
        set => SetProperty(ref _selectJumpModifier, value);
    }

    private string _nextItemHotkey;
    public string NextItemHotkey
    {
        get => _nextItemHotkey;
        set => SetProperty(ref _nextItemHotkey, value);
    }

    private string _previousItemHotkey;
    public string PreviousItemHotkey
    {
        get => _previousItemHotkey;
        set => SetProperty(ref _previousItemHotkey, value);
    }

    private string _actionsMenuHotkey;
    public string ActionsMenuHotkey
    {
        get => _actionsMenuHotkey;
        set => SetProperty(ref _actionsMenuHotkey, value);
    }

    private string _completeFromSelectionHotkey;
    public string CompleteFromSelectionHotkey
    {
        get => _completeFromSelectionHotkey;
        set => SetProperty(ref _completeFromSelectionHotkey, value);
    }

    private string _quickLookHotkey;
    public string QuickLookHotkey
    {
        get => _quickLookHotkey;
        set => SetProperty(ref _quickLookHotkey, value);
    }

    private string _keywordHistoryPreviousHotkey;
    public string KeywordHistoryPreviousHotkey
    {
        get => _keywordHistoryPreviousHotkey;
        set => SetProperty(ref _keywordHistoryPreviousHotkey, value);
    }

    private string _keywordHistoryNextHotkey;
    public string KeywordHistoryNextHotkey
    {
        get => _keywordHistoryNextHotkey;
        set => SetProperty(ref _keywordHistoryNextHotkey, value);
    }

    private string _keywordHistoryDeleteHotkey;
    public string KeywordHistoryDeleteHotkey
    {
        get => _keywordHistoryDeleteHotkey;
        set => SetProperty(ref _keywordHistoryDeleteHotkey, value);
    }

    private string _openFullWindowHotkey;
    public string OpenFullWindowHotkey
    {
        get => _openFullWindowHotkey;
        set => SetProperty(ref _openFullWindowHotkey, value);
    }

    private string _quickPanelHotkey;

    /// <summary>Global, detected by the hook service rather than by a focused window.</summary>
    public string QuickPanelHotkey
    {
        get => _quickPanelHotkey;
        set => SetProperty(ref _quickPanelHotkey, value);
    }

    private string _stayOpenHotkey;
    public string StayOpenHotkey
    {
        get => _stayOpenHotkey;
        set => SetProperty(ref _stayOpenHotkey, value);
    }

    private string _localSendSendWindowHotkey;
    public string LocalSendSendWindowHotkey
    {
        get => _localSendSendWindowHotkey;
        set => SetProperty(ref _localSendSendWindowHotkey, value);
    }

    public void Apply()
    {
        var hotkeys = _userSettings.Hotkeys;

        hotkeys.ToggleWindowHotkey = ToggleHotkeyValue;
        hotkeys.AllowHotkeysInFullscreen = AllowHotkeysInFullscreen;
        hotkeys.OpenFullWindowByDefault = OpenFullWindowByDefault;
        hotkeys.QuickSwitchHotkey = QuickSwitchComboHotkey;

        hotkeys.QuickNavTriggerOnDoubleClick = QuickNavTriggerOnDoubleClick;
        hotkeys.QuickNavTriggerOnMiddleClick = QuickNavTriggerOnMiddleClick;

        hotkeys.SelectJumpModifier = SelectJumpModifier;
        hotkeys.NextItemHotkey = NextItemHotkey;
        hotkeys.PreviousItemHotkey = PreviousItemHotkey;
        hotkeys.ActionsMenuHotkey = ActionsMenuHotkey;
        hotkeys.CompleteFromSelectionHotkey = CompleteFromSelectionHotkey;
        hotkeys.QuickLookHotkey = QuickLookHotkey;
        hotkeys.KeywordHistoryPreviousHotkey = KeywordHistoryPreviousHotkey;
        hotkeys.KeywordHistoryNextHotkey = KeywordHistoryNextHotkey;
        hotkeys.KeywordHistoryDeleteHotkey = KeywordHistoryDeleteHotkey;
        hotkeys.OpenFullWindowHotkey = OpenFullWindowHotkey;
        hotkeys.LocalSendSendWindowHotkey = LocalSendSendWindowHotkey;
        hotkeys.StayOpenHotkey = StayOpenHotkey;
        hotkeys.QuickPanelHotkey = QuickPanelHotkey;

        var pluginActionHotkeys = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in PluginActionGroups)
        {
            foreach (var item in group.Items)
            {
                if (item.HotkeyValue == item.DefaultHotkey) continue; // matches the built-in default -- no override needed
                if (!pluginActionHotkeys.TryGetValue(item.PluginId, out var pluginOverrides))
                {
                    pluginOverrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    pluginActionHotkeys[item.PluginId] = pluginOverrides;
                }
                pluginOverrides[item.ActionId] = item.HotkeyValue;
            }
        }
        hotkeys.PluginActionHotkeys = pluginActionHotkeys;

        _userSettings.Save();

        // Notify hook service process via IPC to reload settings!
        App.HookClient?.SendMessage(new IpcMessage { Id = IpcMessageId.ReloadSettings });
    }

    public void Cleanup() => TranslationManager.Instance.PropertyChanged -= _translationHandler;
}
