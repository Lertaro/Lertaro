using Lertaro.Core;
// This test file's namespace ends in QuickSearchWindow, which is also the window's type name, so an
// unaliased reference binds to the namespace instead of the class and fails to compile. The class itself
// lives in Lertaro.App, not in a namespace matching its folder.
using QuickWindow = Lertaro.App.QuickSearchWindow;

namespace Lertaro.App.Tests.Views.QuickSearchWindow;

// The gate behind the "Lock position" setting, for the logo drag -- which is now the window's only drag:
// the card's own drag, and with it the second and differently-gated path this file used to cover, was
// removed rather than fixed (#255). Only the decision is covered: the drag itself needs a live window,
// which is why the decision was pulled out of the handler in the first place.
[TestClass]
public sealed class LockPositionGateTests
{
    [TestMethod]
    public void UnlockedIsTheOldBehaviour() =>
        // Off by default, so this is what every existing install keeps doing.
        Assert.IsTrue(QuickWindow.ShouldAllowIconDrag(lockPosition: false));

    [TestMethod]
    public void LockedRefusesTheDrag() => Assert.IsFalse(QuickWindow.ShouldAllowIconDrag(lockPosition: true));

    [TestMethod]
    public void TheSettingDefaultsToUnlocked() =>
        // Being able to move the window is what everyone already has; turning that off for them on
        // upgrade would be a change nobody asked for.
        Assert.IsFalse(new SearchWindowSettings().LockPosition);
}
