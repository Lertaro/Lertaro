using System.IO;
using Lertaro.App.Services.AppWindow;

namespace Lertaro.App.Tests.Services.AppWindow;

[TestClass]
public sealed class AppWindowManagerTests
{
    [TestMethod]
    public void DetermineSearchWindowHotkeyAction_NoFullWindow_LeavesItToTheCaller() =>
        // Not "show the full window": the quick-vs-full choice belongs to the OpenFullWindowByDefault
        // setting, so the decision tree stops here and the caller makes that call.
        Assert.AreEqual(AppWindowManager.SearchWindowHotkeyAction.NoFullWindowOnScreen,
            Decide(isVisible: false, isActive: false));

    [TestMethod]
    public void DetermineSearchWindowHotkeyAction_VisibleButUnfocused_BringsItToFront() =>
        // Still on screen, just behind something else -- the summon key means "come back to it" here.
        Assert.AreEqual(AppWindowManager.SearchWindowHotkeyAction.BringToFront,
            Decide(isVisible: true, isActive: false));

    [TestMethod]
    public void DetermineSearchWindowHotkeyAction_FocusedFullWindow_ReturnsToQuickSearch() =>
        // The default: the second press while the full window holds foreground sends the user back to
        // the quick window with their query, so the key cycles between the two.
        Assert.AreEqual(AppWindowManager.SearchWindowHotkeyAction.ReturnToQuickSearch,
            Decide(isVisible: true, isActive: true, closeOnRepeatHotkey: false));

    [TestMethod]
    public void DetermineSearchWindowHotkeyAction_FocusedFullWindowWithCloseSetting_ClosesIt() =>
        Assert.AreEqual(AppWindowManager.SearchWindowHotkeyAction.CloseFullWindow,
            Decide(isVisible: true, isActive: true, closeOnRepeatHotkey: true));

    [TestMethod]
    public void DetermineSearchWindowHotkeyAction_CloseSettingDoesNotChangeTheUnfocusedCase() =>
        // A visible but unfocused full window still means "come back to it"; closing a window the user is
        // reaching for would be the opposite of the request, so the setting must not reach this branch.
        Assert.AreEqual(AppWindowManager.SearchWindowHotkeyAction.BringToFront,
            Decide(isVisible: true, isActive: false, closeOnRepeatHotkey: true));

    [TestMethod]
    public void SummonHotkeyChecksForTheFullWindowBeforeTheOpenFullWindowSetting()
    {
        // The full window is reachable with OpenFullWindowByDefault OFF -- every route there passes
        // through FileExecutor's "__SHOW_MORE__" (the quick window's expand, "show more", and
        // ReopenAsFullWindowOnRepeatHotkey), which is exactly how the reporter had it configured. Gating
        // the full-window branch behind that setting meant the third press fell through to the QUICK
        // window's own visibility toggle: the full window stayed put and the key read as doing nothing.
        var manager = Source("App/Services/AppWindow/AppWindowManager.cs");
        var app = Source("App/App.xaml.cs");

        // The decision has to live where the full window is known about, i.e. the manager.
        Assert.Contains("HandleGlobalSummonHotkey", app,
            "the app must route the summon hotkey through the full-window-aware handler");
        Assert.DoesNotContain("OpenFullWindowByDefault", Between(app, "HookClient.OnActivated", "OnQuickPanelHotkey"),
            "the full window must be considered before that setting, not after it");

        // And within the handler the full window comes first, with the setting consulted only after the
        // no-full-window case falls through.
        var decide = Between(manager, "public static void HandleGlobalSummonHotkey()", "internal enum SearchWindowHotkeyAction");
        var fullWindowBranch = decide.IndexOf("ReturnToQuickSearch", StringComparison.Ordinal);
        var settingBranch = decide.IndexOf("OpenFullWindowByDefault", StringComparison.Ordinal);
        Assert.IsGreaterThan(-1, fullWindowBranch, "the return-to-quick branch is missing");
        Assert.IsGreaterThan(fullWindowBranch, settingBranch,
            "the visible-full-window cases have to be handled before the setting is read");
    }

    private static AppWindowManager.SearchWindowHotkeyAction Decide(bool isVisible, bool isActive, bool closeOnRepeatHotkey = false) =>
        AppWindowManager.DetermineSearchWindowHotkeyAction(isVisible, isActive, closeOnRepeatHotkey);

    private static string Between(string source, string from, string to)
    {
        var start = source.IndexOf(from, StringComparison.Ordinal);
        Assert.IsGreaterThan(-1, start, $"could not find '{from}'");
        var end = source.IndexOf(to, start + from.Length, StringComparison.Ordinal);
        Assert.IsGreaterThan(-1, end, $"could not find '{to}' after '{from}'");
        return source.Substring(start, end - start);
    }

    private static string Source(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AGENTS.md")))
            dir = dir.Parent;
        Assert.IsNotNull(dir, "could not locate the repository root");
        var path = Path.Combine(dir!.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.IsTrue(File.Exists(path), $"expected a file at {path}");
        return File.ReadAllText(path);
    }
}
