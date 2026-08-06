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
}
