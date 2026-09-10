using Lertaro.Core.Hook.InlineSearch;

namespace Lertaro.Core.Tests.Hook.InlineSearch;

[TestClass]
public sealed class ModifierKeyStateTests
{
    [TestMethod]
    public void ModifierKeyDownAndUp_TracksEachModifier()
    {
        var state = new ModifierKeyState();

        state.OnKeyDown(0xA2);
        state.OnKeyDown(0xA4);
        state.OnKeyDown(0xA0);
        state.OnKeyDown(0x5B);

        Assert.IsTrue(state.IsControlDown);
        Assert.IsTrue(state.IsAltDown);
        Assert.IsTrue(state.IsShiftDown);
        Assert.IsTrue(state.IsWindowsDown);
        Assert.IsTrue(state.HasControlAltOrWindowsDown);

        state.OnKeyUp(0xA2);
        state.OnKeyUp(0xA4);
        state.OnKeyUp(0xA0);
        state.OnKeyUp(0x5B);

        Assert.IsFalse(state.IsControlDown);
        Assert.IsFalse(state.IsAltDown);
        Assert.IsFalse(state.IsShiftDown);
        Assert.IsFalse(state.IsWindowsDown);
        Assert.IsFalse(state.HasControlAltOrWindowsDown);
    }

    [TestMethod]
    public void ReleasingOneSideOfModifier_LeavesOtherSideDown()
    {
        var state = new ModifierKeyState();

        state.OnKeyDown(0xA2);
        state.OnKeyDown(0xA3);
        state.OnKeyUp(0xA2);

        Assert.IsTrue(state.IsControlDown);

        state.OnKeyUp(0xA3);

        Assert.IsFalse(state.IsControlDown);
    }

    [TestMethod]
    public void UnrelatedKey_DoesNotChangeModifierState()
    {
        var state = new ModifierKeyState();

        state.OnKeyDown(0x41);
        state.OnKeyUp(0x41);

        Assert.IsFalse(state.IsControlDown);
        Assert.IsFalse(state.IsAltDown);
        Assert.IsFalse(state.IsShiftDown);
        Assert.IsFalse(state.IsWindowsDown);
    }
}
