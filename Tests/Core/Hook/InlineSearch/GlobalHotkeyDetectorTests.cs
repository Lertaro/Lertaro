using Lertaro.Core.Hook;
using Lertaro.Core.Hook.InlineSearch;

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
}
