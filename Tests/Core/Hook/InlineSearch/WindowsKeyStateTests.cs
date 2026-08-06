using Lertaro.Core.Hook.InlineSearch;

namespace Lertaro.Core.Tests.Hook.InlineSearch;

[TestClass]
public sealed class WindowsKeyStateTests
{
    [TestMethod]
    public void WindowsKeyDown_FollowedByOem3_RemainsDownUntilReleased()
    {
        var state = new WindowsKeyState();

        state.OnKeyDown(0x5B);
        state.OnKeyDown(0xC0);

        Assert.IsTrue(state.IsDown);

        state.OnKeyUp(0x5B);

        Assert.IsFalse(state.IsDown);
    }

    [TestMethod]
    public void ReleasingOneWindowsKey_LeavesTheOtherKeyTracked()
    {
        var state = new WindowsKeyState();
        state.OnKeyDown(0x5B);
        state.OnKeyDown(0x5C);

        state.OnKeyUp(0x5B);

        Assert.IsTrue(state.IsDown);
    }
}
