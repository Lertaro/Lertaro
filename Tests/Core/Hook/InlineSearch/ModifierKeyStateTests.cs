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

    [TestMethod]
    public void Synchronize_RemovesModifierStateLostAcrossDesktopTransition()
    {
        var state = new ModifierKeyState();
        state.OnKeyDown(0x5B);
        state.OnKeyDown(0xA4);

        var physicallyDown = new HashSet<int> { 0xA4 };
        state.Synchronize(physicallyDown.Contains);

        Assert.IsFalse(state.IsWindowsDown);
        Assert.IsTrue(state.IsAltDown);
    }

    [TestMethod]
    [DataRow(0x41)]
    [DataRow(0xA3)]
    [DataRow(0xA4)]
    [DataRow(0x01)]
    [DataRow(0x7B)] // F12 is not part of the high-function-key exclusion.
    [DataRow(0x88)]
    public void Synchronize_AllKeys_DetectsAnAlreadyHeldKeyOrMouseButton(int heldKey)
    {
        var state = new ModifierKeyState();
        state.Synchronize(key => key == heldKey, includeAllKeys: true);
        state.OnKeyDown(0xA2);
        Assert.IsTrue(state.HasOtherKeyDown(0xA2));

        state.Synchronize(_ => false, includeAllKeys: true);
        state.OnKeyDown(0xA2);
        Assert.IsFalse(state.HasOtherKeyDown(0xA2));
        Assert.IsTrue(state.IsControlDown, "Apply the current event after the async snapshot.");
    }

    [TestMethod]
    public void Synchronize_GenericControlAlias_DoesNotCancelPureLeftControl()
    {
        var state = new ModifierKeyState();
        state.Synchronize(key => key is 0x11 or 0xA2, includeAllKeys: true);
        Assert.IsFalse(state.HasOtherKeyDown(0xA2));

        state.OnKeyUp(0xA2);
        Assert.IsFalse(state.IsControlDown);
    }

    public static IEnumerable<int> HighFunctionKeys => Enumerable.Range(0x7C, 12);

    [TestMethod]
    [DynamicData(nameof(HighFunctionKeys))]
    public void HasOtherKeyDown_HighFunctionKeyFromEventOrSnapshot_DoesNotBlockTap(int heldKey)
    {
        var state = new ModifierKeyState();
        state.OnKeyDown(heldKey);
        state.OnKeyDown(0xA2);
        Assert.IsFalse(state.HasOtherKeyDown(0xA2));

        state.Synchronize(key => key == heldKey, includeAllKeys: true);
        state.OnKeyDown(0xA2);
        Assert.IsFalse(state.HasOtherKeyDown(0xA2));
        Assert.IsTrue(state.IsControlDown);

        state.OnKeyDown(0x56);
        Assert.IsTrue(state.HasOtherKeyDown(0xA2), "Ignoring a function key must not hide Ctrl+V.");
    }
}
