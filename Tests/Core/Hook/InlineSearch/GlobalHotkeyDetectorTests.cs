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
}
