using Lertaro.Core.Hook.InlineSearch;

namespace Lertaro.Core.Tests.Hook.InlineSearch;

[TestClass]
public sealed class KeyboardUtilsTests
{
    [TestMethod]
    public void GetKeyVirtualCode_NullOrEmpty_ReturnsZero()
    {
        Assert.AreEqual(0, KeyboardUtils.GetKeyVirtualCode(""));
        Assert.AreEqual(0, KeyboardUtils.GetKeyVirtualCode(null!));
    }

    [TestMethod]
    [DataRow("SPACE", 0x20)]
    [DataRow("TAB", 0x09)]
    [DataRow("ENTER", 0x0D)]
    [DataRow("RETURN", 0x0D)]
    [DataRow("ESC", 0x1B)]
    [DataRow("ESCAPE", 0x1B)]
    [DataRow("BACK", 0x08)]
    [DataRow("BACKSPACE", 0x08)]
    [DataRow("CAPSLOCK", 0x14)]
    public void GetKeyVirtualCode_NamedKeys_ReturnExpectedCode(string key, int expected) => Assert.AreEqual(expected, KeyboardUtils.GetKeyVirtualCode(key));

    [TestMethod]
    [DataRow("OEM3", 0xC0)]
    [DataRow("OEMMINUS", 0xBD)]
    [DataRow("OEM7", 0xDE)]
    public void GetKeyVirtualCode_OemKeys_ReturnExpectedCode(string key, int expected) => Assert.AreEqual(expected, KeyboardUtils.GetKeyVirtualCode(key));

    [TestMethod]
    [DataRow("HOME", 0x24)]
    [DataRow("END", 0x23)]
    [DataRow("PAGEUP", 0x21)]
    [DataRow("PRIOR", 0x21)]
    [DataRow("PAGEDOWN", 0x22)]
    [DataRow("NEXT", 0x22)]
    [DataRow("INSERT", 0x2D)]
    [DataRow("DELETE", 0x2E)]
    [DataRow("LEFT", 0x25)]
    [DataRow("UP", 0x26)]
    [DataRow("RIGHT", 0x27)]
    [DataRow("DOWN", 0x28)]
    public void GetKeyVirtualCode_NavigationKeys_ReturnExpectedCode(string key, int expected) => Assert.AreEqual(expected, KeyboardUtils.GetKeyVirtualCode(key));

    [TestMethod]
    public void GetKeyVirtualCode_IsCaseInsensitiveAndTrimmed()
    {
        Assert.AreEqual(0x20, KeyboardUtils.GetKeyVirtualCode("  space  "));
        Assert.AreEqual((int)'A', KeyboardUtils.GetKeyVirtualCode("a"));
    }

    [TestMethod]
    public void GetKeyVirtualCode_SingleLetterOrDigit_ReturnsItsOwnCode()
    {
        Assert.AreEqual((int)'A', KeyboardUtils.GetKeyVirtualCode("A"));
        Assert.AreEqual((int)'5', KeyboardUtils.GetKeyVirtualCode("5"));
    }

    [TestMethod]
    [DataRow("F1", 0x70)]
    [DataRow("F12", 0x7B)]
    public void GetKeyVirtualCode_FunctionKeys_ReturnExpectedCode(string key, int expected) => Assert.AreEqual(expected, KeyboardUtils.GetKeyVirtualCode(key));

    [TestMethod]
    public void GetKeyVirtualCode_FunctionKeyOutOfRange_ReturnsZero()
    {
        Assert.AreEqual(0, KeyboardUtils.GetKeyVirtualCode("F13"));
        Assert.AreEqual(0, KeyboardUtils.GetKeyVirtualCode("F0"));
    }

    [TestMethod]
    public void GetKeyVirtualCode_UnknownKey_ReturnsZero() => Assert.AreEqual(0, KeyboardUtils.GetKeyVirtualCode("NOTAKEY"));

    [TestMethod]
    public void IsModifierKey_ControlVariants_MatchAnyControlVkCode()
    {
        Assert.IsTrue(KeyboardUtils.IsModifierKey(0x11, "CONTROL"));
        Assert.IsTrue(KeyboardUtils.IsModifierKey(0xA2, "CTRL")); // left control
        Assert.IsTrue(KeyboardUtils.IsModifierKey(0xA3, "Ctrl")); // right control
    }

    [TestMethod]
    public void IsModifierKey_AltShiftWin_MatchTheirOwnVkCodes()
    {
        Assert.IsTrue(KeyboardUtils.IsModifierKey(0x12, "ALT"));
        Assert.IsTrue(KeyboardUtils.IsModifierKey(0x10, "SHIFT"));
        Assert.IsTrue(KeyboardUtils.IsModifierKey(0x5B, "WIN"));
        Assert.IsTrue(KeyboardUtils.IsModifierKey(0x5C, "WINDOWS"));
    }

    [TestMethod]
    public void IsModifierKey_MismatchedVkCode_ReturnsFalse()
    {
        Assert.IsFalse(KeyboardUtils.IsModifierKey(0x41, "CONTROL")); // 'A' key
        Assert.IsFalse(KeyboardUtils.IsModifierKey(0x11, "ALT"));
    }

    [TestMethod]
    public void IsModifierKey_UnknownModifierName_ReturnsFalse() => Assert.IsFalse(KeyboardUtils.IsModifierKey(0x11, "BOGUS"));

    [TestMethod]
    public void IsModifierKey_NullModifier_DefaultsToControl() => Assert.IsTrue(KeyboardUtils.IsModifierKey(0x11, null!));

    [TestMethod]
    public void CheckModifiersMatch_UsesHookOwnedModifierState()
    {
        var state = new ModifierKeyState();
        state.OnKeyDown(0xA2);
        state.OnKeyDown(0xA4);

        Assert.IsTrue(KeyboardUtils.CheckModifiersMatch("Ctrl+Alt", state, "NONE"));
        Assert.IsFalse(KeyboardUtils.CheckModifiersMatch("Ctrl", state, "NONE"));

        state.OnKeyUp(0xA4);

        Assert.IsTrue(KeyboardUtils.CheckModifiersMatch("Ctrl", state, "NONE"));
    }

    // The parse is cached by the spec string now, so these pin that the cached answer is exactly what the
    // per-keystroke string walk produced -- including the spellings that must stay inert. The mask is
    // passed as an int because the enum lives inside an internal type.
    [DataTestMethod]
    [DataRow("Ctrl", (int)KeyboardUtils.ModifierMask.Control)]
    [DataRow("CONTROL", (int)KeyboardUtils.ModifierMask.Control)]
    [DataRow(" ctrl ", (int)KeyboardUtils.ModifierMask.Control)]
    [DataRow("Alt", (int)KeyboardUtils.ModifierMask.Alt)]
    [DataRow("Shift", (int)KeyboardUtils.ModifierMask.Shift)]
    [DataRow("Win", (int)KeyboardUtils.ModifierMask.Windows)]
    [DataRow("WINDOWS", (int)KeyboardUtils.ModifierMask.Windows)]
    [DataRow("Ctrl+Alt", (int)(KeyboardUtils.ModifierMask.Control | KeyboardUtils.ModifierMask.Alt))]
    [DataRow("Shift+Win", (int)(KeyboardUtils.ModifierMask.Shift | KeyboardUtils.ModifierMask.Windows))]
    [DataRow("Ctrl++Alt", (int)(KeyboardUtils.ModifierMask.Control | KeyboardUtils.ModifierMask.Alt))]
    [DataRow("", (int)KeyboardUtils.ModifierMask.None)]
    [DataRow("NONE", (int)KeyboardUtils.ModifierMask.None)]
    [DataRow("BOGUS", (int)KeyboardUtils.ModifierMask.None)]
    public void ParseModifiers_MatchesWhatTheStringWalkProduced(string spec, int expected) =>
        Assert.AreEqual((KeyboardUtils.ModifierMask)expected, KeyboardUtils.ParseModifiers(spec));

    [TestMethod]
    public void ParseModifiers_NullSpecIsNoModifierUntilTheCallerDefaultsIt() =>
        Assert.AreEqual(KeyboardUtils.ModifierMask.None, KeyboardUtils.ParseModifiers(null));

    [TestMethod]
    public void BuildKeyState_ReportsOnlyTheShiftThatIsActuallyDown()
    {
        var state = KeyboardUtils.BuildKeyState(vk => vk == 0xA1, _ => false); // right shift held

        Assert.AreEqual(0x80, state[0xA1]);
        Assert.AreEqual(0x80, state[0x10], "the generic VK_SHIFT bit is what ToUnicode reads");
        Assert.AreEqual(0, state[0xA0]);
    }

    [TestMethod]
    public void BuildKeyState_KeepsEveryModifierThePhysicalStateCarries()
    {
        var down = new HashSet<int> { 0xA2, 0xA4, 0x5B }; // left ctrl, left alt, left win

        var state = KeyboardUtils.BuildKeyState(down.Contains, _ => false);

        Assert.AreEqual(0x80, state[0x11]);
        Assert.AreEqual(0x80, state[0xA2]);
        Assert.AreEqual(0x80, state[0x12]);
        Assert.AreEqual(0x80, state[0xA4]);
        Assert.AreEqual(0x80, state[0x5B]);
        Assert.AreEqual(0, state[0x10], "nothing here is a shift");
    }

    [TestMethod]
    public void BuildKeyState_ReadsTheLockKeysAsTogglesNotAsHeldKeys()
    {
        var state = KeyboardUtils.BuildKeyState(_ => false, vk => vk == 0x14); // caps lock on

        Assert.AreEqual(1, state[0x14], "bit 0 is the toggle; the high bit would say caps lock is held down");
        Assert.AreEqual(0, state[0x90]);
    }

    [TestMethod]
    public void BuildKeyState_AllKeysUpIsAnAllZeroArray()
    {
        var state = KeyboardUtils.BuildKeyState(_ => false, _ => false);

        Assert.HasCount(256, state);
        Assert.IsTrue(state.All(b => b == 0));
    }
}
