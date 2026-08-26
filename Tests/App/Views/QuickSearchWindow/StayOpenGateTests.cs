using System.IO;

namespace Lertaro.App.Tests.Views.QuickSearchWindow;

// The Stay Open hotkey (#197) keeps the quick window on screen when focus moves away, so a query can be
// assembled from text copied out of other windows -- hiding clears the query, so a half-built search was
// lost on every switch away.
//
// The flag has to be honoured by the focus-loss hides and by nothing else. QuickSearchWindowController
// needs a live window and a keyboard hook, so it is not constructible here; what regressed in the
// equivalent preview-window bugs was a hide path that failed to consult the flag, and that is what these
// pin, in the same source-scanning spirit as Tests/Core/RepositoryHygieneTests.
[TestClass]
public sealed class StayOpenGateTests
{
    [TestMethod]
    public void BothFocusLossPathsGoThroughTheGate_NotStraightToHideWindow()
    {
        // Two independent triggers, which is the whole hazard: a flag honoured by only one of them leaks.
        // Window_Deactivated is the safety net; the watcher is the global foreground hook.
        var deactivated = Source("App/Views/QuickSearchWindow/QuickSearchWindow.xaml.cs");
        var controller = Source("App/Views/QuickSearchWindow/Helpers/QuickSearchWindowController.cs");

        Assert.Contains("_controller.HideOnFocusLoss(", deactivated,
            "Window_Deactivated must hide through the gate, or Stay Open leaks on that path");
        Assert.Contains("QuickSearchWindowForegroundWatcher(window, () => HideOnFocusLoss())", controller,
            "the foreground watcher must hide through the gate too");
    }

    [TestMethod]
    public void TheGateIsSeparateFromHideWindow_SoDeliberateHidesStillWork()
    {
        // Pressing Enter to open a result, running an action, or toggling the window away with the global
        // hotkey all call HideWindow directly and must keep working while Stay Open is on -- otherwise the
        // window sits there over the file it just opened.
        var controller = Source("App/Views/QuickSearchWindow/Helpers/QuickSearchWindowController.cs");

        var gate = Between(controller, "public void HideOnFocusLoss(", "public void HideWindow(");
        Assert.Contains("if (_stayOpen)", gate, "the gate is where the flag is consulted");

        var hide = Between(controller, "public void HideWindow(", "void FinishHide()");
        Assert.DoesNotContain("_stayOpen", hide,
            "HideWindow itself must not consult the flag: every deliberate hide runs through it");
    }

    [TestMethod]
    public void TheFlagIsClearedByTheNextRealHide_SoItOnlyCoversOneSummon()
    {
        var controller = Source("App/Views/QuickSearchWindow/Helpers/QuickSearchWindowController.cs");
        var finishHide = Between(controller, "void FinishHide()", "if (_window.Content is UIElement fadeContent)");

        Assert.Contains("_stayOpen = false;", finishHide,
            "leaving it set would turn a per-summon escape into a mode the user cannot see they are in");
    }

    [TestMethod]
    public void TheIndicatorNeverOutranksTheServiceWarning()
    {
        // The logo already means three things. Stay Open must not be mistakable for "service unreachable",
        // and must not hide it: in WPF the LAST matching DataTrigger wins, so stay-open has to come first.
        var xaml = Source("App/Views/Controls/SearchBoxControl.xaml");

        // The control declares two logos: the full window uses the left one and the quick window uses the
        // right one. Both support Stay Open, so verify that both indicator styles are theme-backed.
        var stayOpen = xaml.IndexOf("IsStayOpen, ElementName=root", StringComparison.Ordinal);
        Assert.IsGreaterThan(-1, stayOpen, "the indicator trigger is missing");
        Assert.HasCount(2, System.Text.RegularExpressions.Regex.Matches(xaml, "IsStayOpen, ElementName=root"),
            "the full and quick window logos should both carry the indicator");

        var serviceDown = xaml.IndexOf("IsServiceRunning, ElementName=root", stayOpen, StringComparison.Ordinal);
        Assert.IsGreaterThan(-1, serviceDown, "the service-down trigger should follow it in the same block");
        Assert.IsLessThan(serviceDown, stayOpen, "stay-open must be declared before the service-down trigger");
        // Scoped to the trigger's own setters, between its binding and the next one. Asserting against
        // the whole file would have been satisfied by the comment above the trigger that names the
        // rejected brush -- which is what happened on the first attempt at this: the assertion passed
        // while the markup said something else entirely.
        var indicator = xaml.Substring(stayOpen, serviceDown - stayOpen);

        // WarningBrush and the accent are taken, by the service-down and searching states. SuccessBrush
        // and ErrorBrush are barred for a different reason: they are fixed semantic hues that ignore the
        // palette (every curated theme carries the same three greens), so they sit wrong in otherwise
        // neutral chrome and make all 28 themes render identically here. SuccessBrush was tried and
        // rejected on exactly that.
        foreach (var barred in new[] { "WarningBrush", "AccentColor", "SuccessBrush", "ErrorBrush" })
            Assert.DoesNotContain(barred, indicator, $"the Stay Open indicator must not use {barred}");

        Assert.Contains("DynamicResource", indicator, "and must take its colour from the theme, not a literal");
    }

    [TestMethod]
    public void RefocusingDismissesAShellOverlayFirst_LikeAFullSummonDoes()
    {
        // Refocusing is not a lesser summon: pressing the key while the Start Menu is open has to put the
        // Start Menu away, exactly as the normal path does. This regressed once already, by extracting
        // only the activation tail of ShowWindow and leaving this behind -- and it has to come FIRST,
        // because once the window has been activated GetForegroundWindow() reports us and the check can
        // no longer see the overlay.
        var controller = Source("App/Views/QuickSearchWindow/Helpers/QuickSearchWindowController.cs");
        var focus = Between(controller, "public void FocusWindow()", "private void ActivateAndFocus()");

        Assert.Contains("ShellOverlayDismissHelper.DismissOverlayIfForeground();", focus,
            "refocusing must dismiss a shell light-dismiss overlay, or the Start Menu stays up");

        var dismiss = focus.IndexOf("DismissOverlayIfForeground", StringComparison.Ordinal);
        var touchesWindow = focus.IndexOf("_window.", StringComparison.Ordinal);
        Assert.IsGreaterThan(-1, touchesWindow);
        Assert.IsLessThan(touchesWindow, dismiss,
            "it has to run before anything touches the window, or the foreground check is contaminated");
    }

    [TestMethod]
    public void MiddleClickingTheLogoTogglesIt_OnTheSameLogoThatShowsIt()
    {
        // The second way in, alongside the hotkey. The full window uses the left logo and the quick window
        // uses the right logo, so both visible variants must carry the middle-click handler.
        var xaml = Source("App/Views/Controls/SearchBoxControl.xaml");
        var control = Source("App/Views/Controls/SearchBoxControl.xaml.cs");
        var window = Source("App/Views/QuickSearchWindow/QuickSearchWindow.xaml.cs");

        Assert.HasCount(2, System.Text.RegularExpressions.Regex.Matches(xaml, @"MouseUp=""Icon_MouseUp"""),
            "both window logos should carry the middle-click handler");

        var leftIcon = xaml.IndexOf(@"Grid.Column=""0""", StringComparison.Ordinal);
        var rightIcon = xaml.IndexOf(@"Grid.Column=""3""", StringComparison.Ordinal);
        var firstHandler = xaml.IndexOf(@"MouseUp=""Icon_MouseUp""", StringComparison.Ordinal);
        var secondHandler = xaml.IndexOf(@"MouseUp=""Icon_MouseUp""", firstHandler + 1, StringComparison.Ordinal);
        var afterLeft = xaml.IndexOf("</Grid>", leftIcon, StringComparison.Ordinal);
        var afterRight = xaml.IndexOf("</Grid>", rightIcon, StringComparison.Ordinal);
        Assert.IsGreaterThan(-1, leftIcon, "could not find the left icon grid");
        Assert.IsGreaterThan(-1, rightIcon, "could not find the right icon grid");
        Assert.IsGreaterThan(-1, firstHandler, "could not find the left icon handler");
        Assert.IsGreaterThan(-1, secondHandler, "could not find the right icon handler");
        Assert.IsLessThan(firstHandler, leftIcon, "the first handler must be on the left icon");
        Assert.IsLessThan(afterLeft, firstHandler, "the left handler must be inside the left icon grid");
        Assert.IsLessThan(secondHandler, rightIcon, "the second handler must be on the right icon");
        Assert.IsLessThan(afterRight, secondHandler, "the right handler must be inside the right icon grid");

        // WPF has no middle-button event, so the filter has to be explicit or every click would toggle.
        Assert.Contains("e.ChangedButton != MouseButton.Middle", control,
            "the handler must filter to the middle button");
        Assert.Contains("SearchBox.IconMiddleClicked += _controller.ToggleStayOpen;", window,
            "and the quick window must be what it toggles");
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

    private static string Between(string source, string from, string to)
    {
        var start = source.IndexOf(from, StringComparison.Ordinal);
        Assert.IsGreaterThan(-1, start, $"could not find '{from}'");
        var end = source.IndexOf(to, start + from.Length, StringComparison.Ordinal);
        Assert.IsGreaterThan(-1, end, $"could not find '{to}' after '{from}'");
        return source.Substring(start, end - start);
    }
}
