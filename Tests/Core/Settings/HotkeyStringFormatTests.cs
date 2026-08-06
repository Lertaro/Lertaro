namespace Lertaro.Core.Tests.Settings;

[TestClass]
public sealed class HotkeyStringFormatTests
{
    [TestMethod]
    [DataRow("Ctrl", true, "Control")]
    [DataRow("Alt", true, "Alt")]
    [DataRow("Shift", true, "Shift")]
    [DataRow("Win", true, "Win")]
    [DataRow("ctrl", true, "Control")] // case-insensitive
    [DataRow("Ctrl+G", false, "")]
    [DataRow("", false, "")]
    [DataRow(null, false, "")]
    public void IsBareModifier_DetectsBareModifierTokens(string? value, bool expectedIsBare, string expectedModifier)
    {
        var isBare = HotkeyStringFormat.IsBareModifier(value, out var modifier);

        Assert.AreEqual(expectedIsBare, isBare);
        Assert.AreEqual(expectedModifier, modifier);
    }

    [TestMethod]
    public void ParseCombo_ModifierPlusKey_SplitsBoth()
    {
        HotkeyStringFormat.ParseCombo("Ctrl+G", out var modifier, out var key);

        Assert.AreEqual("Control", modifier);
        Assert.AreEqual("G", key);
    }

    [TestMethod]
    public void ParseCombo_NonCtrlModifier_PassesThroughUnchanged()
    {
        HotkeyStringFormat.ParseCombo("Alt+P", out var modifier, out var key);

        Assert.AreEqual("Alt", modifier);
        Assert.AreEqual("P", key);
    }

    [TestMethod]
    public void ParseCombo_MultipleModifiers_PreservesEveryModifier()
    {
        HotkeyStringFormat.ParseCombo("Ctrl+Win+F1", out var modifier, out var key);

        Assert.AreEqual("Control+Win", modifier);
        Assert.AreEqual("F1", key);
    }

    [TestMethod]
    public void ParseCombo_BareModifierToken_IsModifierWithEmptyKey()
    {
        HotkeyStringFormat.ParseCombo("Ctrl", out var modifier, out var key);

        Assert.AreEqual("Control", modifier);
        Assert.AreEqual(string.Empty, key);
    }

    [TestMethod]
    public void ParseCombo_BareNonModifierToken_IsKeyWithEmptyModifier()
    {
        HotkeyStringFormat.ParseCombo("P", out var modifier, out var key);

        Assert.AreEqual(string.Empty, modifier);
        Assert.AreEqual("P", key);
    }

    [TestMethod]
    public void ParseCombo_EmptyValue_ReturnsEmptyBoth()
    {
        HotkeyStringFormat.ParseCombo(null, out var modifier, out var key);

        Assert.AreEqual(string.Empty, modifier);
        Assert.AreEqual(string.Empty, key);
    }

    [TestMethod]
    [DataRow("Oem1", ";")]
    [DataRow("OemPlus", "=")]
    [DataRow("OemComma", ",")]
    [DataRow("OemPeriod", ".")]
    public void ToDisplayText_OemKey_ShowsSymbol(string oemKey, string expectedSymbol) => Assert.AreEqual(expectedSymbol, HotkeyStringFormat.ToDisplayText(oemKey));

    [TestMethod]
    public void ToDisplayText_ComboWithOemKey_ReplacesOnlyTheKeyPart() => Assert.AreEqual("Ctrl+;", HotkeyStringFormat.ToDisplayText("Ctrl+Oem1"));

    [TestMethod]
    public void ToDisplayText_NonOemKey_IsUnchanged() => Assert.AreEqual("Ctrl+G", HotkeyStringFormat.ToDisplayText("Ctrl+G"));

    [TestMethod]
    public void ToDisplayText_EmptyValue_ReturnsEmpty() => Assert.AreEqual(string.Empty, HotkeyStringFormat.ToDisplayText(string.Empty));

    [TestMethod]
    [DataRow("Win")]
    [DataRow("Win+E")]
    [DataRow("Win+Shift+S")]
    [DataRow("Win+Alt+B")]
    [DataRow("Win+Alt+Enter")]
    [DataRow("Win+Ctrl+D")]
    [DataRow("Win+Ctrl+Shift+B")]
    [DataRow("Win+Ctrl+F4")]
    [DataRow("Win+Ctrl+Left")]
    [DataRow("Win+Ctrl+Right")]
    [DataRow("Win+OemComma")]
    [DataRow("Win+Oem2")]
    [DataRow("Win+PrintScreen")]
    [DataRow("Win+1")]
    [DataRow("Win+Alt+1")]
    [DataRow("Win+Ctrl+1")]
    [DataRow("Win+Ctrl+Shift+1")]
    [DataRow("Win+Shift+1")]
    public void IsReservedWindowsShortcut_DetectsDocumentedWindowsShortcuts(string hotkey) =>
        Assert.IsTrue(HotkeyStringFormat.IsReservedWindowsShortcut(hotkey));

    [TestMethod]
    [DataRow("Win+F1")]
    [DataRow("Win+Ctrl+Alt+F1")]
    [DataRow("Ctrl+E")]
    [DataRow("F1")]
    public void IsReservedWindowsShortcut_AllowsOtherShortcuts(string hotkey) =>
        Assert.IsFalse(HotkeyStringFormat.IsReservedWindowsShortcut(hotkey));
}
