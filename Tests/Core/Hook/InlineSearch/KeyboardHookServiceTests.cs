using System.Runtime.InteropServices;
using Lertaro.Core.Hook;
using Lertaro.Core.Hook.InlineSearch;
using Lertaro.PluginSdk.Abstractions.Plugins.WindowAdapters;

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
    [DataRow("Ctrl", 0xA2, 0x100, 0x101, 0u)]
    [DataRow("Ctrl", 0xA2, 0x100, 0x101, 0x10u)]
    [DataRow("Ctrl", 0xA3, 0x100, 0x101, 0x12u)]
    [DataRow("Alt", 0xA4, 0x104, 0x105, 0x10u)]
    [DataRow("Shift", 0xA0, 0x100, 0x101, 0x10u)]
    public void HookCallbackCore_PureDoubleTap_TriggersOnlyOnSecondRelease(
        string modifier, int key, int down, int up, uint flags)
    {
        using var service = CreateService(modifier);
        var summons = 0;
        service.OnDoubleCtrl += () => summons++;

        Feed(service, key, down, 1000, flags);
        Feed(service, key, up, 1010, flags);
        Feed(service, key, down, 1150, flags);
        Assert.AreEqual(0, summons);
        var consumed = Feed(service, key, up, 1160, flags);

        Assert.AreEqual(1, summons);
        Assert.AreEqual(IntPtr.Zero, consumed, "The modifier release must reach the host.");
        Assert.IsTrue(service._hotkeyDetector.CheckModifiersMatch("NONE"));
    }

    [TestMethod]
    [DataRow(0u, 0x10u)]
    [DataRow(0x10u, 0u)]
    public void HookCallbackCore_MixedSourceTransitions_CompleteTheGesture(uint downFlags, uint upFlags)
    {
        using var service = CreateService("Ctrl");
        var summons = 0;
        service.OnDoubleCtrl += () => summons++;
        Feed(service, 0xA2, 0x100, 1000, downFlags);
        Feed(service, 0xA2, 0x101, 1010, upFlags);
        Feed(service, 0xA2, 0x100, 1150, downFlags);
        Feed(service, 0xA2, 0x101, 1160, upFlags);
        Assert.AreEqual(1, summons);
    }

    [TestMethod]
    public void HookCallbackCore_AhkMenuMaskThenPaste_DoesNotSummonOrPrimeNextTap()
    {
        using var service = CreateService("Ctrl");
        var summons = 0;
        service.OnDoubleCtrl += () => summons++;

        Feed(service, 0xA4, 0x104, 9859, 0);
        Feed(service, 0x5A, 0x100, 9984, 0);
        Feed(service, 0x5A, 0x101, 10000, 0);
        Feed(service, 0xA4, 0x105, 10047, 0);
        CtrlTap(service, 10047); // AHK menu-mask Ctrl.
        Feed(service, 0xA2, 0x100, 10219);
        Feed(service, 0x56, 0x100, 10219);
        Feed(service, 0x56, 0x101, 10230);
        Feed(service, 0xA2, 0x101, 10240);
        Assert.AreEqual(0, summons);

        CtrlTap(service, 10370);
        Assert.AreEqual(0, summons, "The cancelled paste chord must not count as the first tap.");
        CtrlTap(service, 10520);
        Assert.AreEqual(1, summons);
    }

    [TestMethod]
    [DataRow(0x41)]
    [DataRow(0x7B)]
    [DataRow(0xA4)]
    [DataRow(0xA0)]
    [DataRow(0x5B)]
    [DataRow(0xA3)]
    public void HookCallbackCore_OtherKeyAlreadyHeld_CancelsBothTaps(int otherKey)
    {
        using var service = CreateService("Ctrl");
        var summons = 0;
        service.OnDoubleCtrl += () => summons++;
        Feed(service, otherKey, 0x100, 900);
        CtrlTap(service, 1000);
        CtrlTap(service, 1150);
        Assert.AreEqual(0, summons);

        Feed(service, otherKey, 0x101, 1165);
        CtrlTap(service, 1300);
        Assert.AreEqual(0, summons);
        CtrlTap(service, 1450);
        Assert.AreEqual(1, summons);
    }

    [TestMethod]
    [DataRow("Ctrl+F2", 0x71)]
    [DataRow("Ctrl+F13", 0x7C)]
    [DataRow("Ctrl+F22", 0x85)]
    [DataRow("Ctrl+F24", 0x87)]
    public void HookCallbackCore_ConsumedQuickPanelChord_CancelsPendingDoubleTap(string hotkey, int key)
    {
        using var service = CreateService("Ctrl");
        service._settings.Hotkeys.QuickPanelHotkey = hotkey;
        var summons = 0;
        var panels = 0;
        service.OnDoubleCtrl += () => summons++;
        service.OnQuickPanelHotkey += () => panels++;
        CtrlTap(service, 1000);
        Feed(service, 0xA2, 0x100, 1150);

        Assert.AreEqual((IntPtr)1, Feed(service, key, 0x100, 1160));
        Feed(service, key, 0x101, 1170);
        Feed(service, 0xA2, 0x101, 1180);
        CtrlTap(service, 1300);

        Assert.AreEqual(1, panels);
        Assert.AreEqual(0, summons);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void HookCallbackCore_DisabledBetweenTaps_ClearsCandidate(bool disableDuringSecondPress)
    {
        using var service = CreateService("Ctrl");
        var summons = 0;
        service.OnDoubleCtrl += () => summons++;
        CtrlTap(service, 1000);
        if (disableDuringSecondPress) Feed(service, 0xA2, 0x100, 1150);
        service.IsHotkeysDisabledTemporarily = true;
        if (disableDuringSecondPress) Feed(service, 0xA2, 0x101, 1160);
        service.IsHotkeysDisabledTemporarily = false;

        CtrlTap(service, 1300);
        Assert.AreEqual(0, summons);
        CtrlTap(service, 1450);
        Assert.AreEqual(1, summons);
    }

    [TestMethod]
    public void NotifyMouseButtonOrWheel_DuringSecondPress_CancelsAndAllowsNextPair()
    {
        using var service = CreateService("Ctrl");
        var summons = 0;
        service.OnDoubleCtrl += () => summons++;
        CtrlTap(service, 1000);
        Feed(service, 0xA2, 0x100, 1150);
        service.NotifyMouseButtonOrWheel();
        Feed(service, 0xA2, 0x101, 1160);
        CtrlTap(service, 1300);
        Assert.AreEqual(0, summons);
        CtrlTap(service, 1450);
        Assert.AreEqual(1, summons);
    }

    [TestMethod]
    public void Stop_WithPendingTap_ClearsCandidate()
    {
        using var service = CreateService("Ctrl");
        var summons = 0;
        service.OnDoubleCtrl += () => summons++;
        CtrlTap(service, 1000);
        service.Stop();
        CtrlTap(service, 1150);
        Assert.AreEqual(0, summons);
    }

    [TestMethod]
    public void HookCallbackCore_InjectedCtrlG_RequestsQuickSwitch()
    {
        using var service = CreateService("Ctrl");
        service.IsQuickSearchWindowVisible = false;
        service._explorerTracker.IsActiveWindowDialog = true;
        service._explorerTracker.ActiveAdapter = new DialogAdapter();
        service._explorerTracker.UpdatePath(@"D:\Work", false, isDialog: false);
        var requests = 0;
        service.OnQuickSwitchRequested += _ => requests++;

        Feed(service, 0xA2, 0x100, 1000);
        Assert.AreEqual((IntPtr)1, Feed(service, 0x47, 0x100, 1010));
        Feed(service, 0x47, 0x101, 1020);
        Feed(service, 0xA2, 0x101, 1030);
        Assert.AreEqual(1, requests);
        Assert.IsTrue(service._hotkeyDetector.CheckModifiersMatch("NONE"));
    }

    [TestMethod]
    public void HookCallbackCore_BareModifierQuickSwitch_RequestsOnReleaseWithoutConsumingCtrl()
    {
        using var service = CreateService("Alt+F9");
        service._settings.Hotkeys.QuickSwitchHotkey = "Ctrl";
        service.IsQuickSearchWindowVisible = false;
        service._explorerTracker.IsActiveWindowDialog = true;
        service._explorerTracker.ActiveAdapter = new DialogAdapter();
        service._explorerTracker.UpdatePath(@"D:\Work", false, isDialog: false);
        var requests = 0;
        service.OnQuickSwitchRequested += _ => requests++;

        CtrlTap(service, 1000);
        Feed(service, 0xA2, 0x100, 1150);
        Assert.AreEqual(0, requests);
        Assert.AreEqual(IntPtr.Zero, Feed(service, 0xA2, 0x101, 1160));
        Assert.AreEqual(1, requests);
    }

    public static IEnumerable<(int Key, string Position, uint Flags, bool QuickSwitch)> HighFunctionInterferenceCases =>
        from key in Enumerable.Range(0x7C, 12)
        from position in new[] { "held", "first", "between", "second" }
        from flags in new[] { 0u, 0x10u }
        from quickSwitch in new[] { false, true }
        select (key, position, flags, quickSwitch);

    [TestMethod]
    [DynamicData(nameof(HighFunctionInterferenceCases))]
    public void HookCallbackCore_HighFunctionInterference_TriggersOnlyOnSecondRelease(
        int key, string position, uint flags, bool quickSwitch)
    {
        using var service = CreateService(quickSwitch ? "Alt+F9" : "Ctrl");
        var activations = 0;
        if (quickSwitch)
        {
            service._settings.Hotkeys.QuickSwitchHotkey = "Ctrl";
            service.IsQuickSearchWindowVisible = false;
            service._explorerTracker.IsActiveWindowDialog = true;
            service._explorerTracker.ActiveAdapter = new DialogAdapter();
            service._explorerTracker.UpdatePath(@"D:\Work", false, isDialog: false);
            service.OnQuickSwitchRequested += _ => activations++;
        }
        else service.OnDoubleCtrl += () => activations++;

        void Interfere(uint time, bool release)
        {
            Assert.AreEqual(IntPtr.Zero, Feed(service, key, 0x100, time, flags));
            if (release) Assert.AreEqual(IntPtr.Zero, Feed(service, key, 0x101, time + 1, flags));
        }

        if (position == "held") Interfere(900, release: false);
        Feed(service, 0xA2, 0x100, 1000);
        if (position == "first") Interfere(1002, release: true);
        Feed(service, 0xA2, 0x101, 1010);
        if (position == "between") Interfere(1100, release: true);
        Feed(service, 0xA2, 0x100, 1150);
        if (position == "second") Interfere(1152, release: true);
        Assert.AreEqual(0, activations);
        Assert.AreEqual(IntPtr.Zero, Feed(service, 0xA2, 0x101, 1160));
        Assert.AreEqual(1, activations);
    }

    private sealed class DialogAdapter : IFileDialogAdapter
    {
        public bool CanHandle(IntPtr hwnd, string className, string processName) => true;
        public string? GetCurrentPath(IntPtr hwnd) => null;
        public bool GetDockBounds(IntPtr hwnd, out AdapterRect rect) { rect = default; return false; }
        public bool RestoreFocus(IntPtr hwnd) => true;
        public bool NavigateTo(IntPtr hwnd, string targetPath) => true;
    }

    private static KeyboardHookService CreateService(string modifier)
    {
        var service = new KeyboardHookService(new ExplorerTracker());
        service._settings.Hotkeys.ToggleWindowHotkey = modifier;
        service._settings.Hotkeys.QuickSwitchHotkey = "Ctrl+G";
        service._settings.Hotkeys.QuickPanelHotkey = "Ctrl+F2";
        service._settings.Hotkeys.QuickNavigationHotkey = "Ctrl+Alt+G";
        service._settings.Hotkeys.AllowHotkeysInFullscreen = true;
        service._settings.BlacklistedProcesses.Clear();
        service.IsQuickSearchWindowVisible = true;
        return service;
    }

    private static void CtrlTap(KeyboardHookService service, uint time)
    {
        Feed(service, 0xA2, 0x100, time);
        Feed(service, 0xA2, 0x101, time + 10);
    }

    private static IntPtr Feed(KeyboardHookService service, int key, int message, uint time, uint flags = 0x10)
    {
        var data = Marshal.AllocHGlobal(Marshal.SizeOf<KeyboardNativeMethods.KBDLLHOOKSTRUCT>());
        try
        {
            Marshal.StructureToPtr(new KeyboardNativeMethods.KBDLLHOOKSTRUCT
            {
                vkCode = (uint)key, flags = flags, time = time
            }, data, false);
            return service.HookCallbackCore(0, (IntPtr)message, data);
        }
        finally { Marshal.FreeHGlobal(data); }
    }
}
