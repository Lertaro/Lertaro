namespace Lertaro.Core.Hook.InlineSearch;

public sealed class GlobalHotkeyDetector
{
    private readonly UserSettings _settings;
    private readonly ExplorerTracker _explorerTracker;

    private readonly ModifierDoubleTapDetector _toggleWindowTapDetector = new();
    private readonly ModifierDoubleTapDetector _quickSwitchTapDetector = new();
    private readonly WindowsKeyState _windowsKeyState = new();

    public GlobalHotkeyDetector(UserSettings settings, ExplorerTracker explorerTracker)
    {
        _settings = settings;
        _explorerTracker = explorerTracker;
    }

    public void OnKeyDown(int vkCode) => _windowsKeyState.OnKeyDown(vkCode);

    // Exposed so KeyboardHookServiceInlineSearchExtensions.HandleInlineSearchTriggerKey can pass the
    // same tracked state into CheckModifiersMatchOnly (SelectJumpModifier) that CheckToggleWindowHotkey/
    // CheckAndHandleQuickSwitch already pass into CheckModifiersMatch above -- otherwise "jump to result
    // N" configured with Win as its modifier would still be exposed to the exact GetKeyState-inside-a-
    // low-level-hook staleness CheckModifiersMatch's own trackedWindowsKeyDown parameter exists to fix.
    public bool IsWindowsKeyDown => _windowsKeyState.IsDown;

    /// <summary>Call on WM_KEYUP / WM_SYSKEYUP to reset the "was released" flags.</summary>
    public void OnKeyUp(int vkCode)
    {
        _windowsKeyState.OnKeyUp(vkCode);
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
                if (KeyboardUtils.CheckModifiersMatch(modifier, _windowsKeyState.IsDown))
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
        if (!KeyboardUtils.CheckModifiersMatch(modifier, _windowsKeyState.IsDown)) return false;

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
                if (KeyboardUtils.CheckModifiersMatch(modifier, _windowsKeyState.IsDown))
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
        lock (_explorerTracker.StateLock)
        {
            if (_explorerTracker.IsActiveWindowDialog && triggered && _explorerTracker.ActiveAdapter != null)
            {
                var lastExplorerPath = _explorerTracker.LastActiveExplorerPath;
                var isValid = !string.IsNullOrEmpty(lastExplorerPath) && Path.IsPathRooted(lastExplorerPath);

                if (isValid)
                {
                    var navPath = lastExplorerPath!.EndsWith("\\") ? lastExplorerPath : lastExplorerPath + "\\";
                    var adapter = _explorerTracker.ActiveAdapter;
                    var hwnd = _explorerTracker.ActiveHwnd;
                    ThreadPool.QueueUserWorkItem(_ =>
                    {
                        // The HWND can be recycled after the snapshot; do not navigate a dead window.
                        if (ExplorerNativeHooks.IsWindow(hwnd))
                            adapter.NavigateTo(hwnd, navPath);
                    });
                    consumeKey = true;
                    return true;
                }
            }
        }
        return false;
    }
}
