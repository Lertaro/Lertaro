using Lertaro.App.Services.ShellMenu.QuickNav;
using Lertaro.PluginSdk.Abstractions.Plugins.WindowAdapters;

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
    private sealed class FakeAdapter(bool canHandle, bool canRecognizeHost, bool isFileExplorer) : IInlineSearchAdapter
    {
        public bool IsFileExplorer => isFileExplorer;
        public bool CanHandle(IntPtr hwnd, string className, string processName) => canHandle;
        public bool CanRecognizeHost(IntPtr hwnd, string className, string processName) => canRecognizeHost;
        public bool CanTrigger(IntPtr focusedHwnd, string className) => false;
        public string? GetSearchScope(IntPtr hwnd) => null;
        public bool ExecuteItem(IntPtr hwnd, string path, string searchInput) => false;
        public bool GetDockBounds(IntPtr hwnd, out AdapterRect rect) { rect = default; return false; }
        public bool CanEnterActionsMode(IntPtr hwnd) => false;
    }

    [TestMethod]
    public void FileManagerHostRecognitionDoesNotDependOnInlineSearchBeingEnabled()
    {
        // CanHandle deliberately represents the EnableInlineSearch setting, while Quick Navigation has
        // its own setting and must still recognize the same host when inline search is off.
        var adapter = new FakeAdapter(canHandle: false, canRecognizeHost: true, isFileExplorer: true);

        var result = QuickNavigationTriggerGate.FindFileManagerAdapter([adapter], new IntPtr(1), "host", "manager");

        Assert.AreSame(adapter, result);
    }

    [TestMethod]
    public void NonFileManagerAdaptersAreNotUsedForQuickNavigation()
    {
        var adapter = new FakeAdapter(canHandle: false, canRecognizeHost: true, isFileExplorer: false);

        var result = QuickNavigationTriggerGate.FindFileManagerAdapter([adapter], new IntPtr(1), "host", "manager");

        Assert.IsNull(result);
    }

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
