namespace Lertaro.Core.Hook.InlineSearch;

// Detects a double-tap of a single modifier key within a (100ms, 300ms) window. Reused by
// GlobalHotkeyDetector for both the toggle-window and quick-switch hotkeys, which previously each
// carried their own copy of this exact state machine.
internal sealed class ModifierDoubleTapDetector
{
    private const int DoubleTapClickCount = 2;

    private uint _lastDownTime;
    private int _lastVkCode;
    private int _clickCount;
    private bool _wasReleased = true;

    /// <summary>Call on WM_KEYUP / WM_SYSKEYUP for the tracked modifier to reset the "was released" flag.</summary>
    public void OnModifierKeyUp() => _wasReleased = true;

    /// <summary>
    /// Feed a WM_KEYDOWN for the tracked modifier's vkCode. Returns true once the double-tap completes.
    /// </summary>
    public bool OnModifierKeyDown(int vkCode, uint time)
    {
        // Key-repeat: the key was never released since last press — ignore
        if (!_wasReleased)
            return false;
        _wasReleased = false;

        var elapsed = time - _lastDownTime;
        if (vkCode == _lastVkCode && elapsed > 100 && elapsed < 300)
        {
            _clickCount++;
            if (_clickCount >= DoubleTapClickCount)
            {
                _clickCount = 0;
                _lastDownTime = 0;
                _lastVkCode = 0;
                return true;
            }
            _lastDownTime = time;
            return false;
        }

        _clickCount = 1;
        _lastDownTime = time;
        _lastVkCode = vkCode;
        return false;
    }

    /// <summary>Call when a WM_KEYDOWN for something other than the tracked modifier arrives.</summary>
    public void ResetOnOtherKey()
    {
        _clickCount = 0;
        _lastDownTime = 0;
        _lastVkCode = 0;
    }
}
