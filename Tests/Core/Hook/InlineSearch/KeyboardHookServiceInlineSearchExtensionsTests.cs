using Lertaro.Core.Hook.InlineSearch;

namespace Lertaro.Core.Tests.Hook.InlineSearch;

// Ctrl+K while Lertaro's inline window covers a file dialog: the one key that has to reach our own window
// while the dialog holds the keyboard, so the App can put the caret in that window's search box. Only the
// decision is covered -- raising the event and focusing the box need a live hook and a live window.
[TestClass]
public sealed class KeyboardHookServiceInlineSearchExtensionsTests
{
    private const int AnotherKey = 0x47; // G

    [TestMethod]
    public void ShouldHandFocusToInlineSearch_InlineWindowOverADialog_IsOurChord() =>
        Assert.IsTrue(ShouldHandFocus());

    [TestMethod]
    public void ShouldHandFocusToInlineSearch_NoInlineWindowOnScreen_HasNothingToFocus() =>
        Assert.IsFalse(ShouldHandFocus(inlineWindowOnScreen: false));

    [TestMethod]
    public void ShouldHandFocusToInlineSearch_AnExplorerWindowRatherThanADialog_LeavesExplorersOwnCtrlFAlone() =>
        // Docked to a plain Explorer window, Ctrl+K is Explorer's "search this folder" and not ours to take.
        Assert.IsFalse(ShouldHandFocus(activeWindowIsDialog: false));

    [TestMethod]
    public void ShouldHandFocusToInlineSearch_QuickWindowIsUp_LeavesItsOpenFullWindowHotkeyAlone() =>
        // The quick window's own OpenFullWindowHotkey defaults to the same Ctrl+K, and that one is handled by
        // the WPF key path; the hook must not answer for a window that is not the one in front of the user.
        Assert.IsFalse(ShouldHandFocus(quickSearchWindowVisible: true));

    [TestMethod]
    public void ShouldHandFocusToInlineSearch_AnyOtherKeyWithCtrl_IsNotOurs() =>
        Assert.IsFalse(ShouldHandFocus(vkCode: AnotherKey));

    [TestMethod]
    public void ShouldHandFocusToInlineSearch_CtrlWithAnotherModifier_IsNotOurs() =>
        // Shift/Alt/Win held as well is a different chord, and one the host may well own.
        Assert.IsFalse(ShouldHandFocus(controlOnlyDown: false));

    private static bool ShouldHandFocus(
        bool inlineWindowOnScreen = true,
        bool activeWindowIsDialog = true,
        bool quickSearchWindowVisible = false,
        int vkCode = KeyboardNativeMethods.VK_F,
        bool controlOnlyDown = true) =>
        KeyboardHookServiceInlineSearchExtensions.ShouldHandFocusToInlineSearch(
            inlineWindowOnScreen, activeWindowIsDialog, quickSearchWindowVisible, vkCode, controlOnlyDown);
}
