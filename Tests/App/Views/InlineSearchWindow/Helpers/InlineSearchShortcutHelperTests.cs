using Lertaro.App.Views.InlineSearchWindow.Helpers;

namespace Lertaro.App.Tests.Views.InlineSearchWindow.Helpers;

// Regression coverage: PR #185 made HotkeyStringFormat.ParseCombo preserve every modifier instead of
// just the first one, so its modifier output can now be a "+"-joined combo like "Control+Win" rather
// than always a single word. AbbreviateModifiers used to only match the whole string against "Control"
// (via string.Equals), which silently stopped abbreviating to "Ctrl" the moment a second modifier (most
// commonly Win) joined the combo -- e.g. the quick-switch hint would show "Control+Win+G" instead of
// "Ctrl+Win+G".
[TestClass]
public sealed class InlineSearchShortcutHelperTests
{
    [TestMethod]
    public void AbbreviateModifiers_SingleControl_AbbreviatesToCtrl() =>
        Assert.AreEqual("Ctrl", InlineSearchShortcutHelper.AbbreviateModifiers("Control"));

    [TestMethod]
    public void AbbreviateModifiers_ControlCombinedWithWin_AbbreviatesOnlyTheControlSegment() =>
        Assert.AreEqual("Ctrl+Win", InlineSearchShortcutHelper.AbbreviateModifiers("Control+Win"));

    [TestMethod]
    public void AbbreviateModifiers_WinBeforeControl_AbbreviatesRegardlessOfOrder() =>
        Assert.AreEqual("Win+Ctrl", InlineSearchShortcutHelper.AbbreviateModifiers("Win+Control"));

    [TestMethod]
    public void AbbreviateModifiers_NoControlSegment_LeavesOtherModifiersUnchanged() =>
        Assert.AreEqual("Alt+Shift", InlineSearchShortcutHelper.AbbreviateModifiers("Alt+Shift"));

    [TestMethod]
    public void AbbreviateModifiers_EmptyString_ReturnsEmpty() =>
        Assert.AreEqual(string.Empty, InlineSearchShortcutHelper.AbbreviateModifiers(""));
}
