namespace Lertaro.Core.Hook.InlineSearch;

public sealed class GlobalHotkeyDetector
{
    private readonly UserSettings _settings;
    private readonly ExplorerTracker _explorerTracker;

    private readonly ModifierDoubleTapDetector _toggleWindowTapDetector = new();
    private readonly ModifierDoubleTapDetector _quickSwitchTapDetector = new();
    private readonly ModifierKeyState _modifierKeyState = new();

    /// <summary>
    /// Where a recognized Quick Switch is handed off. The hook cannot do the work itself: deciding needs the
    /// tracker lock, which the WinEvent thread holds across cross-process reads, and the navigation calls into
    /// another process. Production posts a thread message to the tracker thread (see HookProcess).
    /// </summary>
    private readonly Action<IntPtr> _requestQuickSwitch;

    public GlobalHotkeyDetector(UserSettings settings, ExplorerTracker explorerTracker, Action<IntPtr>? requestQuickSwitch = null)
    {
        _settings = settings;
        _explorerTracker = explorerTracker;
        // Without a tracker thread to post to, the same policy runs here: that is what tests get, and what any
        // caller that only needs the gesture answered gets.
        _requestQuickSwitch = requestQuickSwitch
            ?? (hwnd => QuickSwitchNavigator.Execute(_explorerTracker, hwnd));
    }

    public void OnKeyDown(int vkCode)
    {
        _modifierKeyState.OnKeyDown(vkCode);
        CancelTapsForOtherKey(vkCode);
    }

    // Call before applying the incoming event: async key state still describes the previous event.
    internal void SynchronizeModifierState(int vkCode)
    {
        static bool IsDown(int key) => (KeyboardNativeMethods.GetAsyncKeyState(key) & 0x8000) != 0;
        _modifierKeyState.Synchronize(IsDown, IsDoubleTapKey(vkCode));
        _toggleWindowTapDetector.Synchronize(IsDown);
        _quickSwitchTapDetector.Synchronize(IsDown);
    }

    private static bool IsTapKey(int vkCode, string hotkey) =>
        HotkeyStringFormat.IsBareModifier(hotkey, out var modifier) && KeyboardUtils.IsModifierKey(vkCode, modifier);

    internal bool IsDoubleTapKey(int vkCode) => IsTapKey(vkCode, _settings.Hotkeys.ToggleWindowHotkey)
        || IsTapKey(vkCode, _settings.Hotkeys.QuickSwitchHotkey);

    private void CancelTapsForOtherKey(int vkCode)
    {
        if (ModifierKeyState.IsIgnoredForDoubleTap(vkCode)) return;
        // Observe every event before a different shortcut can consume it.
        if (!IsTapKey(vkCode, _settings.Hotkeys.ToggleWindowHotkey)) _toggleWindowTapDetector.ResetOnOtherKey();
        if (!IsTapKey(vkCode, _settings.Hotkeys.QuickSwitchHotkey)) _quickSwitchTapDetector.ResetOnOtherKey();
    }

    internal void CancelDoubleTaps()
    {
        _toggleWindowTapDetector.ResetOnOtherKey();
        _quickSwitchTapDetector.ResetOnOtherKey();
    }

    internal void ResetDoubleTaps()
    {
        _toggleWindowTapDetector.Reset();
        _quickSwitchTapDetector.Reset();
    }

    internal bool HasControlAltOrWindowsDown => _modifierKeyState.HasControlAltOrWindowsDown;

    internal bool CheckModifiersMatch(string expectedModifier) =>
        KeyboardUtils.CheckModifiersMatch(expectedModifier, _modifierKeyState, "NONE");

    internal bool CheckModifiersMatchOnly(string expectedModifier) =>
        KeyboardUtils.CheckModifiersMatch(expectedModifier, _modifierKeyState, "CONTROL");

    /// <summary>Update state before checking release-triggered hotkeys.</summary>
    public void OnKeyUp(int vkCode)
    {
        _modifierKeyState.OnKeyUp(vkCode);
        CancelTapsForOtherKey(vkCode);
    }

    public bool CheckToggleWindowHotkey(int vkCode, uint time, out bool consumeKey, Action? onDoubleCtrl, bool isKeyUp = false)
    {
        consumeKey = false;
        var triggered = false;
        if (HotkeyStringFormat.IsBareModifier(_settings.Hotkeys.ToggleWindowHotkey, out var clickModifier))
        {
            if (KeyboardUtils.IsModifierKey(vkCode, clickModifier))
            {
                triggered = CheckModifierTap(_toggleWindowTapDetector, vkCode, time, isKeyUp);
            }
            else if (!ModifierKeyState.IsIgnoredForDoubleTap(vkCode))
            {
                _toggleWindowTapDetector.ResetOnOtherKey();
            }
        }
        else if (!isKeyUp)
        {
            HotkeyStringFormat.ParseCombo(_settings.Hotkeys.ToggleWindowHotkey, out var modifier, out var key);
            var targetVk = KeyboardUtils.GetKeyVirtualCode(key);
            if (targetVk != 0 && vkCode == targetVk)
            {
                if (CheckModifiersMatch(modifier))
                {
                    CancelDoubleTaps();
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

        // A configured chord takes precedence over an unfinished modifier double tap, including F13-F24.
        CancelDoubleTaps();
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

        CancelDoubleTaps();
        consumeKey = true;
        return true;
    }

    public bool CheckAndHandleQuickSwitch(int vkCode, uint time, out bool consumeKey, bool isKeyUp = false)
    {
        consumeKey = false;
        var triggered = false;
        var isBareModifier = HotkeyStringFormat.IsBareModifier(_settings.Hotkeys.QuickSwitchHotkey, out var clickModifier);
        if (isBareModifier)
        {
            if (KeyboardUtils.IsModifierKey(vkCode, clickModifier))
            {
                triggered = CheckModifierTap(_quickSwitchTapDetector, vkCode, time, isKeyUp);
            }
            else if (!ModifierKeyState.IsIgnoredForDoubleTap(vkCode))
            {
                _quickSwitchTapDetector.ResetOnOtherKey();
            }
        }
        else if (!isKeyUp)
        {
            HotkeyStringFormat.ParseCombo(_settings.Hotkeys.QuickSwitchHotkey, out var modifier, out var key);
            var targetVk = KeyboardUtils.GetKeyVirtualCode(key);
            if (targetVk != 0 && vkCode == targetVk)
            {
                if (CheckModifiersMatch(modifier))
                {
                    CancelDoubleTaps();
                    triggered = true;
                }
            }
        }

        var handled = TryHandleQuickSwitchNavigation(triggered, out consumeKey);
        if (isBareModifier) consumeKey = false;
        return handled;
    }

    private bool CheckModifierTap(ModifierDoubleTapDetector detector, int vkCode, uint time, bool isKeyUp)
    {
        if (isKeyUp) return detector.OnModifierKeyUp(vkCode, time);
        detector.OnModifierKeyDown(vkCode, time);
        if (_modifierKeyState.HasOtherKeyDown(vkCode)) detector.ResetOnOtherKey();
        return false;
    }

    // Quick Switch's trigger doesn't just toggle a window like the other hotkey does -- it re-navigates
    // the active (dialog) Explorer-like window back to the last folder that was active outside it. Kept as
    // its own method so the gesture-detection above (shared via ModifierDoubleTapDetector) and this
    // navigation policy read as two separate steps, even though they still live in the same class.
    private bool TryHandleQuickSwitchNavigation(bool triggered, out bool consumeKey)
    {
        consumeKey = false;
        if (!triggered) return false;

        // Read without the lock, deliberately. These fields are written under StateLock, but waiting for it
        // here used to drop the keystroke -- the WinEvent thread holds it across cross-process reads, and a
        // low-level hook that overruns LowLevelHooksTimeout is removed by Windows. A stale answer costs one
        // skipped or one futile hand-off, never a wrong window: the tracker thread re-reads the state before
        // it navigates (see QuickSwitchNavigator.Execute).
        if (!_explorerTracker.IsActiveWindowDialog || _explorerTracker.ActiveAdapter == null)
        {
            Logger.Log("[GlobalHotkeyDetector] Quick Switch ignored: no file dialog is tracked.", LogLevel.Info);
            return false;
        }

        // Nothing to switch to, so the keystroke is left to the host window: a dialog that swallows Ctrl+G
        // without navigating is a keystroke the user cannot get back.
        if (string.IsNullOrEmpty(_explorerTracker.LastActiveExplorerPath) && _explorerTracker.LastPathProvider == null)
        {
            Logger.Log("[GlobalHotkeyDetector] Quick Switch ignored: no browsed folder is known yet.", LogLevel.Info);
            return false;
        }

        _requestQuickSwitch(_explorerTracker.ActiveHwnd);
        consumeKey = true;
        return true;
    }
}
