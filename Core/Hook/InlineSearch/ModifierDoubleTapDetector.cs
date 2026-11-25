namespace Lertaro.Core.Hook.InlineSearch;

// Confirm both taps on release: the second Ctrl down may still become Ctrl+V (#555).
internal sealed class ModifierDoubleTapDetector
{
    private int _pressedKey;
    private uint _downTime;
    private int _firstTapKey;
    private uint _firstDownTime;
    private bool _secondTap;
    private bool _cancelled;

    public void OnModifierKeyDown(int vkCode, uint time)
    {
        if (_pressedKey != 0)
        {
            if (_pressedKey != vkCode) ResetOnOtherKey();
            return; // Auto-repeat is still the same press, including after cancellation.
        }

        var elapsed = unchecked(time - _firstDownTime);
        _secondTap = vkCode == _firstTapKey && elapsed > 100 && elapsed < 300;
        _firstTapKey = 0;
        _pressedKey = vkCode;
        _downTime = time;
        _cancelled = false;
    }

    public bool OnModifierKeyUp(int vkCode, uint time)
    {
        if (vkCode != _pressedKey)
        {
            ResetOnOtherKey();
            return false;
        }

        _pressedKey = 0;
        // Keep the existing down-to-down window, and reject a hold on either tap.
        if (_cancelled || unchecked(time - _downTime) >= 300)
        {
            ResetOnOtherKey();
            return false;
        }

        if (_secondTap)
        {
            ResetOnOtherKey();
            return true;
        }

        _firstTapKey = vkCode;
        _firstDownTime = _downTime;
        return false;
    }

    public void ResetOnOtherKey()
    {
        _firstTapKey = 0;
        _secondTap = false;
        _cancelled = true;
    }

    public void Reset()
    {
        _pressedKey = 0;
        ResetOnOtherKey();
    }

    // A secure-desktop transition can hide a release. Do not keep ignoring fresh presses forever.
    internal void Synchronize(Func<int, bool> isKeyDown)
    {
        if (_pressedKey != 0 && !isKeyDown(_pressedKey)) Reset();
    }
}
