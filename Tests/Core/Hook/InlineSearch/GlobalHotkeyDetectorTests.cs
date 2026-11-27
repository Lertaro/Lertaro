using Lertaro.Core.Hook;
using Lertaro.Core.Hook.InlineSearch;
using Lertaro.PluginSdk.Abstractions.Plugins.WindowAdapters;

namespace Lertaro.Core.Tests.Hook.InlineSearch;

[TestClass]
public sealed class GlobalHotkeyDetectorTests
{
    [TestMethod]
    public void QuickPanelHotkey_RemainsAvailableAfterAnotherCtrlChord()
    {
        var settings = new UserSettings();
        settings.Hotkeys.QuickPanelHotkey = "Ctrl+F2";
        var detector = new GlobalHotkeyDetector(settings, new ExplorerTracker());

        detector.OnKeyDown(0xA2);
        Assert.IsFalse(detector.CheckQuickPanelHotkey(0x4E, out _));
        detector.OnKeyUp(0x4E);
        detector.OnKeyUp(0xA2);

        detector.OnKeyDown(0xA2);
        var triggered = detector.CheckQuickPanelHotkey(0x71, out var consumeKey);

        Assert.IsTrue(triggered);
        Assert.IsTrue(consumeKey);
    }

    [TestMethod]
    public void ToggleHotkey_RecognizesTrackedAltState()
    {
        var settings = new UserSettings();
        settings.Hotkeys.ToggleWindowHotkey = "Alt+Space";
        var detector = new GlobalHotkeyDetector(settings, new ExplorerTracker());

        detector.OnKeyDown(0xA4);
        var triggered = detector.CheckToggleWindowHotkey(0x20, 1000, out var consumeKey, null);

        Assert.IsTrue(triggered);
        Assert.IsTrue(consumeKey);
    }

    [TestMethod]
    public void QuickPanelHotkey_RecognizesMultipleTrackedModifiers()
    {
        var settings = new UserSettings();
        settings.Hotkeys.QuickPanelHotkey = "Ctrl+Alt+Shift+Win+F2";
        var detector = new GlobalHotkeyDetector(settings, new ExplorerTracker());

        detector.OnKeyDown(0xA2);
        detector.OnKeyDown(0xA4);
        detector.OnKeyDown(0xA0);
        detector.OnKeyDown(0x5B);

        var triggered = detector.CheckQuickPanelHotkey(0x71, out var consumeKey);

        Assert.IsTrue(triggered);
        Assert.IsTrue(consumeKey);
    }

    // The chord predicates the inline window's Alt+Space suppression consults: they must answer for a
    // configured summon chord without consuming it or moving the double-tap detectors' state, because
    // the detection that will consume it runs later on the same keystroke.
    [TestMethod]
    public void ToggleComboPredicate_AnswersTheChordWithoutTouchingTapState()
    {
        var settings = new UserSettings();
        settings.Hotkeys.ToggleWindowHotkey = "Alt+Space";
        var detector = new GlobalHotkeyDetector(settings, new ExplorerTracker());

        Assert.IsFalse(detector.IsToggleWindowComboDown(0x20));
        detector.OnKeyDown(0xA4);
        Assert.IsTrue(detector.IsToggleWindowComboDown(0x20));
        // The modifiers are an exact match: Alt held is the combination, Alt plus anything else is not.
        detector.OnKeyDown(0xA2);
        Assert.IsFalse(detector.IsToggleWindowComboDown(0x20));
        Assert.IsFalse(detector.IsToggleWindowComboDown(0x71));
    }

    [TestMethod]
    public void ToggleComboPredicate_IsFalseForTheBareModifierForm()
    {
        var settings = new UserSettings();
        settings.Hotkeys.ToggleWindowHotkey = "Ctrl";
        var detector = new GlobalHotkeyDetector(settings, new ExplorerTracker());

        detector.OnKeyDown(0xA2);
        Assert.IsFalse(detector.IsToggleWindowComboDown(0x20));
    }

    [TestMethod]
    public void QuickPanelComboPredicate_AnswersItsOwnCombination()
    {
        var settings = new UserSettings();
        settings.Hotkeys.QuickPanelHotkey = "Ctrl+F2";
        var detector = new GlobalHotkeyDetector(settings, new ExplorerTracker());

        detector.OnKeyDown(0xA2);
        Assert.IsTrue(detector.IsQuickPanelComboDown(0x71));
        Assert.IsFalse(detector.IsQuickPanelComboDown(0x20));
    }

    [TestMethod]
    [DataRow("toggle")]
    [DataRow("panel")]
    [DataRow("navigation")]
    [DataRow("switch")]
    public void ConfiguredHighFunctionChord_CancelsPendingDoubleTaps(string action)
    {
        var settings = new UserSettings();
        settings.Hotkeys.ToggleWindowHotkey = action == "toggle" ? "Ctrl+F13" : "Ctrl";
        settings.Hotkeys.QuickSwitchHotkey = action == "switch" ? "Ctrl+F13" : "Ctrl";
        settings.Hotkeys.QuickPanelHotkey = "Ctrl+F13";
        settings.Hotkeys.QuickNavigationHotkey = "Ctrl+F13";
        using var tracker = new ExplorerTracker { IsActiveWindowDialog = true, ActiveAdapter = new DialogAdapter() };
        tracker.UpdatePath(@"D:\Work", false, isDialog: false);
        var detector = new GlobalHotkeyDetector(settings, tracker, _ => { });

        bool Control(uint time, bool isUp)
        {
            if (isUp) detector.OnKeyUp(0xA2);
            else detector.OnKeyDown(0xA2);
            return detector.CheckToggleWindowHotkey(0xA2, time, out _, null, isUp)
                | detector.CheckAndHandleQuickSwitch(0xA2, time, out _, isUp);
        }

        Assert.IsFalse(Control(1000, isUp: false));
        Assert.IsFalse(Control(1010, isUp: true));
        Assert.IsFalse(Control(1150, isUp: false));
        detector.OnKeyDown(0x7C);
        var consume = false;
        var matched = action switch
        {
            "toggle" => detector.CheckToggleWindowHotkey(0x7C, 1152, out consume, null),
            "panel" => detector.CheckQuickPanelHotkey(0x7C, out consume),
            "navigation" => detector.CheckQuickNavigationHotkey(0x7C, out consume),
            _ => detector.CheckAndHandleQuickSwitch(0x7C, 1152, out consume)
        };
        Assert.IsTrue(matched);
        Assert.IsTrue(consume);
        detector.OnKeyUp(0x7C);

        Assert.IsFalse(Control(1160, isUp: true), "The recognized chord must cancel the second tap.");
        Assert.IsFalse(Control(1300, isUp: false));
        Assert.IsFalse(Control(1310, isUp: true), "The cancelled chord must not prime a new pair.");
        Assert.IsFalse(Control(1450, isUp: false));
        Assert.IsTrue(Control(1460, isUp: true), "A fresh double tap must still work.");
    }

    private sealed class DialogAdapter : IFileDialogAdapter
    {
        public bool CanHandle(IntPtr hwnd, string className, string processName) => true;
        public string? GetCurrentPath(IntPtr hwnd) => null;
        public bool GetDockBounds(IntPtr hwnd, out AdapterRect rect) { rect = default; return false; }
        public bool RestoreFocus(IntPtr hwnd) => true;
        public bool NavigateTo(IntPtr hwnd, string targetPath) => true;
    }
}
