using System.Runtime.InteropServices;
using Lertaro.Core.Hook;
using Lertaro.Core.Hook.InlineSearch;

namespace Lertaro.Core.Tests.Hook.InlineSearch;

[TestClass]
[DoNotParallelize]
public sealed class KeyboardHookServiceTests
{
    [TestInitialize]
    public void Initialize() => UserSettings.ForceReload();

    [TestCleanup]
    public void Cleanup() => UserSettings.ForceReload();

    [TestMethod]
    [DataRow("Ctrl", 0xA2, 0x100, 0x101)]
    [DataRow("Alt", 0xA4, 0x104, 0x105)]
    public void HookCallbackCore_InjectedDoubleTap_DoesNotSummonOrPrimeTheNextPhysicalTap(
        string modifier, int key, int downMessage, int upMessage)
    {
        using var service = CreateService(modifier);
        var summons = 0;
        service.OnDoubleCtrl += () => summons++;

        Inject(service, key, downMessage, 1000);
        Inject(service, key, upMessage, 1010);
        Inject(service, key, downMessage, 1150);
        Inject(service, key, upMessage, 1160);

        Assert.AreEqual(0, summons);
        service._hotkeyDetector.OnKeyDown(key);
        Assert.IsFalse(service._hotkeyDetector.CheckToggleWindowHotkey(key, 1300, out _, () => summons++));
        service._hotkeyDetector.OnKeyUp(key);
        service._hotkeyDetector.OnKeyDown(key);
        Assert.IsTrue(service._hotkeyDetector.CheckToggleWindowHotkey(key, 1450, out _, () => summons++));
        Assert.AreEqual(1, summons);
    }

    [TestMethod]
    [DataRow("Ctrl", 0xA2, 0x101)]
    [DataRow("Alt", 0xA4, 0x105)]
    public void HookCallbackCore_InjectedRelease_DoesNotTurnPhysicalKeyRepeatIntoDoubleTap(
        string modifier, int key, int upMessage)
    {
        using var service = CreateService(modifier);
        var detector = service._hotkeyDetector;
        detector.OnKeyDown(key);
        Assert.IsFalse(detector.CheckToggleWindowHotkey(key, 1000, out _, null));

        Inject(service, key, upMessage, 1050);

        detector.OnKeyDown(key);
        Assert.IsFalse(detector.CheckToggleWindowHotkey(key, 1150, out _, null));
        detector.OnKeyUp(key);
        detector.OnKeyDown(key);
        Assert.IsTrue(detector.CheckToggleWindowHotkey(key, 1200, out _, null));
    }

    [TestMethod]
    [DataRow(0xA2, 0x100, 0x10u)]
    [DataRow(0xA4, 0x104, 0x30u)]
    [DataRow(0xA2, 0x100, 0x12u)]
    public void HookCallbackCore_InjectedModifier_DoesNotChangeChordState(int key, int message, uint flags)
    {
        using var service = CreateService("Ctrl");

        Inject(service, key, message, 1000, flags);

        Assert.IsTrue(service._hotkeyDetector.CheckModifiersMatch("NONE"));
    }

    private static KeyboardHookService CreateService(string modifier)
    {
        var tracker = new ExplorerTracker();
        var settings = new UserSettings();
        settings.Hotkeys.ToggleWindowHotkey = modifier;
        return new KeyboardHookService(tracker)
        {
            _settings = settings,
            _hotkeyDetector = new GlobalHotkeyDetector(settings, tracker)
        };
    }

    private static void Inject(KeyboardHookService service, int key, int message, uint time, uint flags = 0x10)
    {
        var data = Marshal.AllocHGlobal(Marshal.SizeOf<KeyboardNativeMethods.KBDLLHOOKSTRUCT>());
        try
        {
            Marshal.StructureToPtr(new KeyboardNativeMethods.KBDLLHOOKSTRUCT
            {
                vkCode = (uint)key,
                flags = flags,
                time = time
            }, data, false);
            service.HookCallbackCore(0, (IntPtr)message, data);
        }
        finally { Marshal.FreeHGlobal(data); }
    }
}
