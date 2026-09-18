using System.Windows.Input;
using Lertaro.App.Services.Favorites;

namespace Lertaro.App.Tests.Services.Favorites;

[TestClass]
public sealed class FavoriteHotkeyFormatTests
{
    [TestMethod]
    [DataRow("Ctrl+Shift+D")]
    [DataRow("Alt+F1")]
    [DataRow("Ctrl+Alt+Win+K")]
    [DataRow("Ctrl+Oem1")]
    [DataRow("Shift+Enter")]
    [DataRow("Ctrl+;")]
    [DataRow("Ctrl+Comma")]
    public void TryBuild_AcceptsRecorderCombinations(string hotkey) =>
        Assert.IsTrue(FavoriteHotkeyFormat.TryBuild(hotkey, out _, out _));

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("Ctrl")]
    [DataRow("Alt")]
    [DataRow("Win")]
    [DataRow("NotAKey")]
    [DataRow("Ctrl+NotAKey")]
    [DataRow("F12")]
    [DataRow("Ctrl+F12")]
    public void TryBuild_RejectsUnusableCombinations(string? hotkey) =>
        Assert.IsFalse(FavoriteHotkeyFormat.TryBuild(hotkey, out _, out _));

    [TestMethod]
    public void TryBuild_LeavesBothOutputsZeroWhenRefused()
    {
        // A caller that ignored the return value would otherwise register whichever half-built value
        // happened to be left in the out parameters.
        Assert.IsFalse(FavoriteHotkeyFormat.TryBuild("Ctrl", out var virtualKey, out var modifiers));

        Assert.AreEqual(0u, virtualKey);
        Assert.AreEqual(0u, modifiers);
    }

    [TestMethod]
    public void TryBuild_ConvertsKeyNamesToVirtualKeyCodes()
    {
        // Every expectation is cast to uint: the production output is a uint, and an int literal here
        // would make MSTest compare two differently-typed boxed values, which is never equal.
        Assert.IsTrue(FavoriteHotkeyFormat.TryBuild("Ctrl+D", out var d, out _));
        Assert.AreEqual((uint)'D', d);

        Assert.IsTrue(FavoriteHotkeyFormat.TryBuild("Ctrl+1", out var one, out _));
        Assert.AreEqual((uint)'1', one);

        Assert.IsTrue(FavoriteHotkeyFormat.TryBuild("Ctrl+F1", out var f1, out _));
        Assert.AreEqual(0x70u, f1);

        Assert.IsTrue(FavoriteHotkeyFormat.TryBuild("Ctrl+F11", out var f11, out _));
        Assert.AreEqual(0x7Au, f11);

        Assert.IsTrue(FavoriteHotkeyFormat.TryBuild("Ctrl+Enter", out var enter, out _));
        Assert.AreEqual(0x0Du, enter);

        // The recorder stores WPF's own "Oem1" name; a missing mapping here is exactly the bug that made
        // punctuation hotkeys silently never fire elsewhere in the app.
        Assert.IsTrue(FavoriteHotkeyFormat.TryBuild("Ctrl+Oem1", out var oem1, out _));
        Assert.AreEqual(0xBAu, oem1);
    }

    [TestMethod]
    [DataRow("Ctrl+OemComma")]
    [DataRow("Ctrl+Comma")]
    [DataRow("Ctrl+,")]
    public void TryBuild_AcceptsEverySpellingOfAPunctuationKey(string hotkey)
    {
        // The recorder writes WPF's member name, the Settings hint shows the symbol, and either can be
        // hand-edited into the settings file -- all three have to reach the same key.
        Assert.IsTrue(FavoriteHotkeyFormat.TryBuild(hotkey, out var vk, out _));

        Assert.AreEqual(0xBCu, vk);
    }

    [TestMethod]
    public void ToRegisterModifiers_MapsEachModifierFlag()
    {
        Assert.AreEqual(FavoriteHotkeyNativeMethods.ModControl | FavoriteHotkeyNativeMethods.ModNoRepeat,
            FavoriteHotkeyFormat.ToRegisterModifiers(ModifierKeys.Control));
        Assert.AreEqual(FavoriteHotkeyNativeMethods.ModAlt | FavoriteHotkeyNativeMethods.ModNoRepeat,
            FavoriteHotkeyFormat.ToRegisterModifiers(ModifierKeys.Alt));
        Assert.AreEqual(FavoriteHotkeyNativeMethods.ModShift | FavoriteHotkeyNativeMethods.ModNoRepeat,
            FavoriteHotkeyFormat.ToRegisterModifiers(ModifierKeys.Shift));
        Assert.AreEqual(FavoriteHotkeyNativeMethods.ModWin | FavoriteHotkeyNativeMethods.ModNoRepeat,
            FavoriteHotkeyFormat.ToRegisterModifiers(ModifierKeys.Windows));
    }

    [TestMethod]
    public void ToRegisterModifiers_CombinesFlagsAndKeepsNoRepeat()
    {
        var result = FavoriteHotkeyFormat.ToRegisterModifiers(ModifierKeys.Control | ModifierKeys.Shift);

        Assert.AreEqual(FavoriteHotkeyNativeMethods.ModControl | FavoriteHotkeyNativeMethods.ModShift
            | FavoriteHotkeyNativeMethods.ModNoRepeat, result);
    }

    [TestMethod]
    public void ToRegisterModifiers_NoModifierAddsNoNoRepeatBit() =>
        // MOD_NOREPEAT is only meaningful alongside a real modifier, so asking for none must stay none
        // rather than producing the flag on its own.
        Assert.AreEqual(FavoriteHotkeyFormat.NoModifier, FavoriteHotkeyFormat.ToRegisterModifiers(ModifierKeys.None));
}
