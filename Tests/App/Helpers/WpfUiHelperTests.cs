using System.Windows.Input;
using Lertaro.App.Helpers;

namespace Lertaro.App.Tests.Helpers;

[TestClass]
public sealed class WpfUiHelperTests
{
    [TestMethod]
    [DataRow("ALT", ModifierKeys.Alt)]
    [DataRow("shift", ModifierKeys.Shift)]
    [DataRow("WIN", ModifierKeys.Windows)]
    [DataRow("windows", ModifierKeys.Windows)]
    [DataRow("NONE", ModifierKeys.None)]
    [DataRow("CTRL", ModifierKeys.Control)]
    [DataRow("garbage", ModifierKeys.Control)]
    public void GetWpfModifier_MapsKnownStrings(string input, ModifierKeys expected) =>
        Assert.AreEqual(expected, WpfUiHelper.GetWpfModifier(input));

    [TestMethod]
    public void GetWpfModifier_EmptyString_DefaultsToControl() =>
        Assert.AreEqual(ModifierKeys.Control, WpfUiHelper.GetWpfModifier(""));

    [TestMethod]
    public void TryParseHotkey_NullOrWhitespace_ReturnsFalse()
    {
        Assert.IsFalse(WpfUiHelper.TryParseHotkey(null, out _, out _));
        Assert.IsFalse(WpfUiHelper.TryParseHotkey("   ", out _, out _));
    }

    [TestMethod]
    public void TryParseHotkey_SingleKeyNoModifiers_ReturnsTrue()
    {
        var ok = WpfUiHelper.TryParseHotkey("Enter", out var key, out var modifiers);

        Assert.IsTrue(ok);
        Assert.AreEqual(Key.Enter, key);
        Assert.AreEqual(ModifierKeys.None, modifiers);
    }

    [TestMethod]
    public void TryParseHotkey_MultipleModifiersAndKey_CombinesModifiers()
    {
        var ok = WpfUiHelper.TryParseHotkey("Ctrl+Shift+Enter", out var key, out var modifiers);

        Assert.IsTrue(ok);
        Assert.AreEqual(Key.Enter, key);
        Assert.AreEqual(ModifierKeys.Control | ModifierKeys.Shift, modifiers);
    }

    [TestMethod]
    public void TryParseHotkey_DigitKeyPrefixedWithD_ParsesAsDKey()
    {
        // Real recorded hotkeys always come from Key.ToString() (see HotkeyRecorderControl.KeyDisplayName),
        // which spells the digit-5 key as "D5", never a bare "5".
        var ok = WpfUiHelper.TryParseHotkey("Ctrl+D5", out var key, out var modifiers);

        Assert.IsTrue(ok);
        Assert.AreEqual(Key.D5, key);
        Assert.AreEqual(ModifierKeys.Control, modifiers);
    }

    [TestMethod]
    public void TryParseHotkey_BareDigit_ParsesAsNumericEnumOrdinalNotDKey()
    {
        // Enum.TryParse("5", ...) succeeds by interpreting "5" as the raw underlying int value (Key.Clear = 5)
        // rather than falling through to the "D"-prefix fallback branch, which is effectively dead code for
        // any digit whose ordinal happens to name a real Key member.
        var ok = WpfUiHelper.TryParseHotkey("Ctrl+5", out var key, out _);

        Assert.IsTrue(ok);
        Assert.AreEqual(Key.Clear, key);
    }

    [TestMethod]
    public void TryParseHotkey_OnlyModifiers_ReturnsFalse()
    {
        var ok = WpfUiHelper.TryParseHotkey("Ctrl+Shift", out var key, out _);

        Assert.IsFalse(ok);
        Assert.AreEqual(Key.None, key);
    }

    [TestMethod]
    public void TryParseHotkey_UnrecognizedKeyToken_LeavesKeyNoneAndReturnsFalse()
    {
        var ok = WpfUiHelper.TryParseHotkey("Ctrl+NotAKey", out var key, out _);

        Assert.IsFalse(ok);
        Assert.AreEqual(Key.None, key);
    }

    [TestMethod]
    public void MatchesHotkey_MatchingKeyAndModifiers_ReturnsTrue() =>
        Assert.IsTrue(WpfUiHelper.MatchesHotkey("Ctrl+Enter", ModifierKeys.Control, Key.Enter));

    [TestMethod]
    public void MatchesHotkey_DifferentKey_ReturnsFalse() =>
        Assert.IsFalse(WpfUiHelper.MatchesHotkey("Ctrl+Enter", ModifierKeys.Control, Key.Escape));

    [TestMethod]
    public void MatchesHotkey_DifferentModifiers_ReturnsFalse() =>
        Assert.IsFalse(WpfUiHelper.MatchesHotkey("Ctrl+Enter", ModifierKeys.Alt, Key.Enter));

    [TestMethod]
    public void MatchesHotkey_UnparsableStoredHotkey_ReturnsFalse() =>
        Assert.IsFalse(WpfUiHelper.MatchesHotkey(null, ModifierKeys.None, Key.Enter));

    [TestMethod]
    [DataRow(0.0, 58.0, 0)]
    [DataRow(57.9, 58.0, 0)]
    [DataRow(58.0, 58.0, 1)]
    [DataRow(90.0, 58.0, 1)]
    [DataRow(116.0, 58.0, 2)]
    public void GetFirstVisibleIndexFromPixelOffset_ConvertsPixelsToWholeItemIndex(double verticalOffset, double rowHeight, int expected) =>
        Assert.AreEqual(expected, WpfUiHelper.GetFirstVisibleIndexFromPixelOffset(verticalOffset, rowHeight));

    [TestMethod]
    public void GetFirstVisibleIndexFromPixelOffset_ZeroRowHeight_ReturnsZeroInsteadOfDividingByZero() =>
        Assert.AreEqual(0, WpfUiHelper.GetFirstVisibleIndexFromPixelOffset(100.0, 0.0));

    // QuickSearchWindowLayoutManager toggles CanContentScroll at runtime (item-based/virtualized normally,
    // pixel-based only while clipping a partial row to the tab-strip budget), so this must read whichever
    // mode is CURRENTLY set rather than assuming one, or Ctrl+1-9 breaks the moment the other mode is
    // active (this exact area has regressed twice already this way). A bare ScrollViewer never outside a
    // real layout pass always reports VerticalOffset=0 (ScrollableHeight is 0 with no content, so
    // ScrollToVerticalOffset has nothing to clamp into), which is why these only exercise the null case --
    // the mode-dependent arithmetic itself is GetFirstVisibleIndexFromPixelOffset, already covered above.
    [TestMethod]
    public void GetFirstVisibleIndex_NullScrollViewer_ReturnsZero() =>
        Assert.AreEqual(0, WpfUiHelper.GetFirstVisibleIndex(null, 58.0));
}
