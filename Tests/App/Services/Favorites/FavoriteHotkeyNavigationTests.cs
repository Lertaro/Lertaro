using Lertaro.App.Services.Favorites;

namespace Lertaro.App.Tests.Services.Favorites;

[TestClass]
public sealed class FavoriteHotkeyNavigationTests
{
    [TestMethod]
    public void Decide_NavigatesInPlaceForAFolderInASupportedManager() =>
        Assert.AreEqual(
            FavoriteHotkeyNavigationAction.NavigateInPlace,
            FavoriteHotkeyNavigation.Decide(isWebUrl: false, isVirtualPath: false, targetIsFolder: true, managerCanNavigateInPlace: true));

    [TestMethod]
    public void Decide_LocatesAFileInsteadOfNavigatingIntoIt() =>
        // A file has no folder of its own to show, so the route that lands on it in its containing
        // folder is the right one; navigating "into" it would be wrong.
        Assert.AreEqual(
            FavoriteHotkeyNavigationAction.OpenThroughDefaultRoute,
            FavoriteHotkeyNavigation.Decide(isWebUrl: false, isVirtualPath: false, targetIsFolder: false, managerCanNavigateInPlace: true));

    [TestMethod]
    public void Decide_DoesNothingWhenTheForegroundWindowIsNotASupportedManager()
    {
        // The product decision this pins: never open a window over whatever the user is working in.
        Assert.AreEqual(
            FavoriteHotkeyNavigationAction.NotAFileManager,
            FavoriteHotkeyNavigation.Decide(isWebUrl: false, isVirtualPath: false, targetIsFolder: true, managerCanNavigateInPlace: false));
    }

    [TestMethod]
    public void Decide_DoesNothingForAFileWhenNoManagerIsInFront()
    {
        Assert.AreEqual(
            FavoriteHotkeyNavigationAction.NotAFileManager,
            FavoriteHotkeyNavigation.Decide(isWebUrl: false, isVirtualPath: false, targetIsFolder: false, managerCanNavigateInPlace: false));
    }

    [TestMethod]
    public void Decide_RejectsAWebAddressEvenWithAManagerInFront()
    {
        // A favorite can be a URL; no file manager can be navigated to one.
        Assert.AreEqual(
            FavoriteHotkeyNavigationAction.UnusableTarget,
            FavoriteHotkeyNavigation.Decide(isWebUrl: true, isVirtualPath: false, targetIsFolder: false, managerCanNavigateInPlace: true));
    }

    [TestMethod]
    public void Decide_RejectsAnUnresolvedVirtualFolder()
    {
        // A virtual folder the shell could not turn into a real path would otherwise be handed to a
        // manager as a path it cannot navigate to -- the silent-wrong-thing case.
        Assert.AreEqual(
            FavoriteHotkeyNavigationAction.UnusableTarget,
            FavoriteHotkeyNavigation.Decide(isWebUrl: false, isVirtualPath: true, targetIsFolder: false, managerCanNavigateInPlace: true));
    }

    [TestMethod]
    public void Decide_ChecksTheTargetBeforeTheForegroundWindow()
    {
        // Both are unusable at once; the answer must still be about the favorite, not about the window,
        // so a URL favorite never depends on what happens to be focused.
        Assert.AreEqual(
            FavoriteHotkeyNavigationAction.UnusableTarget,
            FavoriteHotkeyNavigation.Decide(isWebUrl: true, isVirtualPath: false, targetIsFolder: true, managerCanNavigateInPlace: false));
    }
}
