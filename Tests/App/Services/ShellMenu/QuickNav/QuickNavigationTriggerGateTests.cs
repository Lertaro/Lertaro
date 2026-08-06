using Lertaro.App.Services.ShellMenu.QuickNav;

namespace Lertaro.App.Tests.Services.ShellMenu.QuickNav;

// Quick Navigation opens on a click in empty space, which the gate establishes by hit-testing the
// desktop's icon list. Turning off "Show desktop icons" hides that list rather than emptying it, so
// the click lands on the wallpaper host instead and the gate stopped recognising the desktop at all.
//
// The gate's own entry point needs real window handles, so what is pinned here is the classification
// that decides the case: which windows are the desktop itself rather than something sitting on it.
[TestClass]
public sealed class QuickNavigationTriggerGateTests
{
    [TestMethod]
    public void TheWallpaperHostsCountAsDesktopBackground()
    {
        // Which of the two answers WindowFromPoint depends on the Windows version and on whether a
        // wallpaper slideshow is running, so neither alone is enough.
        Assert.IsTrue(QuickNavigationTriggerGate.IsDesktopBackgroundClass("Progman"));
        Assert.IsTrue(QuickNavigationTriggerGate.IsDesktopBackgroundClass("WorkerW"));
    }

    [TestMethod]
    public void TheViewLeftBehindByHiddenIconsCountsToo() =>
        // SHELLDLL_DefView is what remains once its SysListView32 child is hidden. Reaching it means the
        // cursor got past the icons without landing on one, which is the case this whole fix is about.
        Assert.IsTrue(QuickNavigationTriggerGate.IsDesktopBackgroundClass("SHELLDLL_DefView"));

    [TestMethod]
    public void ClassNamesAreMatchedRegardlessOfCase()
    {
        // GetClassName returns whatever the class was registered as, and these are quoted with varying
        // case across Microsoft's own documentation; an ordinal comparison here would be a coin toss.
        Assert.IsTrue(QuickNavigationTriggerGate.IsDesktopBackgroundClass("progman"));
        Assert.IsTrue(QuickNavigationTriggerGate.IsDesktopBackgroundClass("WORKERW"));
    }

    [TestMethod]
    public void TheIconListItselfIsNotBackground() =>
        // The load-bearing exclusion. SysListView32 is where icons live, so a click reaching it has to
        // go on to the hit test rather than being waved through as empty space: treating it as
        // background would pop the menu over every icon on the desktop.
        Assert.IsFalse(QuickNavigationTriggerGate.IsDesktopBackgroundClass("SysListView32"));

    [TestMethod]
    public void OrdinaryWindowsAreNotBackground()
    {
        Assert.IsFalse(QuickNavigationTriggerGate.IsDesktopBackgroundClass("DirectUIHWND"));
        Assert.IsFalse(QuickNavigationTriggerGate.IsDesktopBackgroundClass("CabinetWClass"));
        Assert.IsFalse(QuickNavigationTriggerGate.IsDesktopBackgroundClass(string.Empty));
    }
}
