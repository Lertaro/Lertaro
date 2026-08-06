using System.Windows.Input;
using Lertaro.App.Helpers;
using Lertaro.App.Services;
using Lertaro.App.Services.PluginManagerCore;
using Lertaro.Core;

using Lertaro.App.Services.Plugin;
using Lertaro.Core.Wire;
namespace Lertaro.App.ViewModels.Settings;

public class HotkeySettingsViewModel : ViewModelBase
{
    private readonly UserSettings _userSettings;

    public HotkeySettingsViewModel(UserSettings userSettings, BlacklistSettingsViewModel blacklist)
    {
        _userSettings = userSettings;
        Blacklist = blacklist;
        var hotkeys = _userSettings.Hotkeys;

        // Initialize local bindings from user settings
        _toggleHotkeyValue = hotkeys.ToggleWindowHotkey;
        _allowHotkeysInFullscreen = hotkeys.AllowHotkeysInFullscreen;
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

        PluginActionGroups = BuildPluginActionGroups(hotkeys.PluginActionHotkeys);

        // Plugin action DisplayName/plugin Name are read live off the action/plugin objects, so they
        // need an explicit refresh on a runtime language switch (nothing else re-raises them).
        TranslationManager.Instance.PropertyChanged += (s, e) =>
        {
            foreach (var group in PluginActionGroups)
            {
                group.RefreshPluginName();
                foreach (var item in group.Items) item.RefreshDisplayName();
            }
        };
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

    // Internal, not private: also called by SettingsWindowSearchExtensions.BuildAllEntries to build the
    // same groups (in the same PluginManager.Instance.AllActions order) for settings search, both for
    // the in-app search box when no live HotkeySettingsViewModel exists yet and for the SDK-facing
    // SettingsSearchService feed, which never has a live SettingsViewModel at all.
    internal static List<PluginActionGroupViewModel> BuildPluginActionGroups(Dictionary<string, Dictionary<string, string>> overrides)
    {
        var groups = new List<PluginActionGroupViewModel>();
        foreach (var pluginGroup in PluginManager.Instance.AllActions.GroupBy(r => r.Plugin))
        {
            // Matches the plugin ID convention already used by PluginSettings/PluginConfigFieldViewModel:
            // the DLL file name with its extension stripped (e.g. "Lertaro.Plugins.CoreExtensions").
            var pluginId = System.IO.Path.GetFileNameWithoutExtension(ComponentFilter.GetDllName(pluginGroup.Key));
            // Excludes actions that declare Parameters (e.g. TouchAction/MkdirAction's "filename"/
            // "foldername") -- those only make sense invoked by typing their Keywords ("touch foo.txt"),
            // which is where the actual argument comes from. A hotkey press has no such text to supply,
            // and these actions don't override IsVisibleInMenu to offer a parameter-free fallback the way
            // OpenCommandPromptAction does (its own Keywords-based "cmd"/"cmda" typing is one path, but it
            // also stays hotkey-able whenever a single folder is selected) -- so a configured hotkey for
            // one of these would sit in Settings looking functional while never actually firing, in any
            // window, because HotkeyActionTrigger's dispatch (App\Helpers\HotkeyActionTrigger.cs) requires
            // IsVisibleInMenu to be true, whose default implementation is `Keywords.Count == 0`.
            var hotkeyConfigurableActions = pluginGroup.Where(reg => reg.Action.Parameters.Count == 0);
            var items = hotkeyConfigurableActions.Select(reg =>
            {
                var currentValue = overrides.TryGetValue(pluginId, out var pluginOverrides)
                     && pluginOverrides.TryGetValue(reg.Action.GetType().Name, out var overrideValue)
                    ? overrideValue
                    : reg.Action.Hotkey;
                return new PluginActionHotkeyItemViewModel(pluginId, reg.Action, currentValue);
            }).ToList();

            if (items.Count > 0)
                groups.Add(new PluginActionGroupViewModel(pluginGroup.Key, items));
        }
        return groups;
    }

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
}
