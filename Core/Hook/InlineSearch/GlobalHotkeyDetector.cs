namespace Lertaro.Core.Hook.InlineSearch;

public sealed class GlobalHotkeyDetector
{
    private readonly UserSettings _settings;
    private readonly ExplorerTracker _explorerTracker;

    private readonly ModifierDoubleTapDetector _toggleWindowTapDetector = new();
    private readonly ModifierDoubleTapDetector _quickSwitchTapDetector = new();
    private readonly ModifierKeyState _modifierKeyState = new();

    public GlobalHotkeyDetector(UserSettings settings, ExplorerTracker explorerTracker)
    {
        _settings = settings;
        _explorerTracker = explorerTracker;
    }

    public void OnKeyDown(int vkCode) => _modifierKeyState.OnKeyDown(vkCode);

    // Secure-desktop transitions can hide a modifier key-up from the low-level hook; refresh the
    // hook-owned snapshot before processing the next key event.
    internal void SynchronizeModifierState() => _modifierKeyState.Synchronize(vkCode =>
        (KeyboardNativeMethods.GetAsyncKeyState(vkCode) & 0x8000) != 0);

    internal bool HasControlAltOrWindowsDown => _modifierKeyState.HasControlAltOrWindowsDown;

    internal bool CheckModifiersMatch(string expectedModifier) =>
        KeyboardUtils.CheckModifiersMatch(expectedModifier, _modifierKeyState, "NONE");

    internal bool CheckModifiersMatchOnly(string expectedModifier) =>
        KeyboardUtils.CheckModifiersMatch(expectedModifier, _modifierKeyState, "CONTROL");

    /// <summary>Call on WM_KEYUP / WM_SYSKEYUP to reset the "was released" flags.</summary>
    public void OnKeyUp(int vkCode)
    {
        _modifierKeyState.OnKeyUp(vkCode);
        if (HotkeyStringFormat.IsBareModifier(_settings.Hotkeys.ToggleWindowHotkey, out var toggleModifier) &&
            KeyboardUtils.IsModifierKey(vkCode, toggleModifier))
        {
            _toggleWindowTapDetector.OnModifierKeyUp();
        }

        if (HotkeyStringFormat.IsBareModifier(_settings.Hotkeys.QuickSwitchHotkey, out var quickSwitchModifier) &&
            KeyboardUtils.IsModifierKey(vkCode, quickSwitchModifier))
        {
            _quickSwitchTapDetector.OnModifierKeyUp();
        }
    }

    public bool CheckToggleWindowHotkey(int vkCode, uint time, out bool consumeKey, Action? onDoubleCtrl)
    {
        consumeKey = false;
        var triggered = false;
        if (HotkeyStringFormat.IsBareModifier(_settings.Hotkeys.ToggleWindowHotkey, out var clickModifier))
        {
            if (KeyboardUtils.IsModifierKey(vkCode, clickModifier))
            {
                triggered = _toggleWindowTapDetector.OnModifierKeyDown(vkCode, time);
            }
            else
            {
                _toggleWindowTapDetector.ResetOnOtherKey();
            }
        }
        else
        {
            HotkeyStringFormat.ParseCombo(_settings.Hotkeys.ToggleWindowHotkey, out var modifier, out var key);
            var targetVk = KeyboardUtils.GetKeyVirtualCode(key);
            if (targetVk != 0 && vkCode == targetVk)
            {
                if (CheckModifiersMatch(modifier))
                {
                    triggered = true;
                    consumeKey = true;
                }
            }
        }

        if (triggered)
        {
            onDoubleCtrl?.Invoke();
        }
        return triggered;
    }

    /// <summary>
    /// Whether this keystroke is the toggle hotkey's chord form -- the same test the combo branch of
    /// <see cref="CheckToggleWindowHotkey"/> applies, answered without touching the tap detectors'
    /// state. False for the bare-modifier form, which no chord with a key of its own can be.
    /// </summary>
    internal bool IsToggleWindowComboDown(int vkCode)
    {
        if (HotkeyStringFormat.IsBareModifier(_settings.Hotkeys.ToggleWindowHotkey, out _))
            return false;

        HotkeyStringFormat.ParseCombo(_settings.Hotkeys.ToggleWindowHotkey, out var modifier, out var key);
        var targetVk = KeyboardUtils.GetKeyVirtualCode(key);
        return targetVk != 0 && vkCode == targetVk && CheckModifiersMatch(modifier);
    }

    /// <summary>Whether this keystroke is the quick panel's own combination, answered without consuming it.</summary>
    internal bool IsQuickPanelComboDown(int vkCode)
    {
        HotkeyStringFormat.ParseCombo(_settings.Hotkeys.QuickPanelHotkey, out var modifier, out var key);
        var targetVk = KeyboardUtils.GetKeyVirtualCode(key);
        return targetVk != 0 && vkCode == targetVk && CheckModifiersMatch(modifier);
    }

    /// <summary>The quick panel's own global combo. A plain combination, with no bare-modifier form.</summary>
    /// <remarks>
    /// The tap detectors the other two hotkeys carry exist because those can be configured as a bare
    /// modifier, which needs double-tap timing to tell apart from the same modifier being held down for
    /// something else. This one is always a real key, so there is nothing to disambiguate.
    /// </remarks>
    public bool CheckQuickPanelHotkey(int vkCode, out bool consumeKey)
    {
        consumeKey = false;

        HotkeyStringFormat.ParseCombo(_settings.Hotkeys.QuickPanelHotkey, out var modifier, out var key);
        var targetVk = KeyboardUtils.GetKeyVirtualCode(key);
        if (targetVk == 0 || vkCode != targetVk) return false;
        if (!CheckModifiersMatch(modifier)) return false;

        consumeKey = true;
        return true;
    }

    /// <summary>The global shortcut for opening Quick Navigation in desktop mode.</summary>
    public bool CheckQuickNavigationHotkey(int vkCode, out bool consumeKey)
    {
        consumeKey = false;
        HotkeyStringFormat.ParseCombo(_settings.Hotkeys.QuickNavigationHotkey, out var modifier, out var key);
        var targetVk = KeyboardUtils.GetKeyVirtualCode(key);
        if (targetVk == 0 || vkCode != targetVk || !CheckModifiersMatch(modifier)) return false;

        consumeKey = true;
        return true;
    }

    public bool CheckAndHandleQuickSwitch(int vkCode, uint time, out bool consumeKey)
    {
        consumeKey = false;
        var triggered = false;
        if (HotkeyStringFormat.IsBareModifier(_settings.Hotkeys.QuickSwitchHotkey, out var clickModifier))
        {
            if (KeyboardUtils.IsModifierKey(vkCode, clickModifier))
            {
                triggered = _quickSwitchTapDetector.OnModifierKeyDown(vkCode, time);
            }
            else
            {
                _quickSwitchTapDetector.ResetOnOtherKey();
            }
        }
        else
        {
            HotkeyStringFormat.ParseCombo(_settings.Hotkeys.QuickSwitchHotkey, out var modifier, out var key);
            var targetVk = KeyboardUtils.GetKeyVirtualCode(key);
            if (targetVk != 0 && vkCode == targetVk)
            {
                if (CheckModifiersMatch(modifier))
                {
                    triggered = true;
                }
            }
        }

        return TryHandleQuickSwitchNavigation(triggered, out consumeKey);
    }

    // Quick Switch's trigger doesn't just toggle a window like the other hotkey does -- it re-navigates
    // the active (dialog) Explorer-like window back to the last folder that was active outside it. Kept as
    // its own method so the gesture-detection above (shared via ModifierDoubleTapDetector) and this
    // navigation policy read as two separate steps, even though they still live in the same class.
    private bool TryHandleQuickSwitchNavigation(bool triggered, out bool consumeKey)
    {
        consumeKey = false;
        // Bounded exactly like ExplorerTracker.ReclassifyActiveWindowBounded: this runs inside the
        // low-level keyboard hook callback, while the WinEvent tracker thread holds StateLock across
        // plugin reads budgeted at ExplorerWindowClassifier.DefaultPluginTimeoutMs (2s). Blocking on it
        // here stalls keyboard input for that long, and a hook that overruns LowLevelHooksTimeout is
        // silently dropped by Windows. On contention there is nothing to skip past: the tracker's own
        // state is authoritative and keeps updating, so this keystroke simply does not navigate.
        if (!Monitor.TryEnter(_explorerTracker.StateLock, 50))
        {
            if (triggered)
                Logger.Log("[GlobalHotkeyDetector] Quick Switch declined: tracker state contended.", LogLevel.Info);
            return false;
        }

        try
        {
            if (!_explorerTracker.IsActiveWindowDialog || !triggered || _explorerTracker.ActiveAdapter == null)
            {
                if (triggered)
                    Logger.Log($"[GlobalHotkeyDetector] Quick Switch declined: activeWindowIsDialog={_explorerTracker.IsActiveWindowDialog}, adapter={(_explorerTracker.ActiveAdapter == null ? "null" : "present")}.", LogLevel.Info);
                return false;
            }

            // One resolver decides the destination, so the priority order and the desktop rule live in exactly one
            // place (see QuickSwitchTargetResolver) -- this used to be re-derived here and in the App separately.
            var rememberedPath = QuickSwitchTargetResolver.RememberedPath(_explorerTracker);

            var adapter = _explorerTracker.ActiveAdapter;
            var hwnd = _explorerTracker.ActiveHwnd;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                // Still nothing: this process has not seen a file manager since it started (the usual case
                // right after the App is launched, with the file manager window already open and the dialog
                // in front of it). Ask the collector registry for the open folder windows instead -- off
                // this thread, because that enumeration is COM (ShellWindows) and the keyboard hook callback
                // may not spend that time: Windows silently drops a hook that overruns LowLevelHooksTimeout.
                var navPath = rememberedPath;
                if (string.IsNullOrEmpty(navPath))
                {
                    // Nothing remembered: ask for the file manager that was last activated.
                    navPath = QuickSwitchTargetResolver.FromOpenFolders();
                    if (string.IsNullOrEmpty(navPath))
                    {
                        Logger.Log("[GlobalHotkeyDetector] Quick Switch declined: no remembered path and no open folder window.", LogLevel.Info);
                        return;
                    }

                    Logger.Log($"[GlobalHotkeyDetector] Quick Switch using the open folder '{navPath}' (no remembered path yet).", LogLevel.Info);
                }

                navPath = navPath.EndsWith("\\") ? navPath : navPath + "\\";

                // The HWND can be recycled after the snapshot; do not navigate a dead window.
                if (!ExplorerNativeHooks.IsWindow(hwnd))
                    return;

                // Resolve the adapter from the window itself, exactly as the IPC-driven dialog route does
                // (FileDialogCommandHandler.ResolveAdapter). The tracker's ActiveAdapter was resolved for
                // whatever window it classified, which is not necessarily this one -- and a NavigateTo on the
                // wrong adapter fails silently, which is what made this look like "the hotkey did nothing".
                var className = new System.Text.StringBuilder(256);
                ExplorerNativeHooks.GetClassName(hwnd, className, className.Capacity);
                var byHwnd = PluginSdk.Registries.FileDialogAdapterRegistry.GetMatchingAdapter(
                    hwnd, className.ToString(), _explorerTracker.GetProcessName(hwnd));
                var navigator = byHwnd ?? adapter;
                Logger.Log($"[GlobalHotkeyDetector] Quick Switch navigating hwnd=0x{hwnd.ToInt64():X} to '{navPath}' via '{navigator.Name}' (byHwnd={byHwnd != null}).", LogLevel.Info);
                navigator.NavigateTo(hwnd, navPath);
            });
            consumeKey = true;
            return true;
        }
        finally
        {
            Monitor.Exit(_explorerTracker.StateLock);
        }
    }
}
