using Lertaro.App.Services;

namespace Lertaro.App.Tests.Services.InlineSearch;

// The hook flags are the only inline-search state that lives outside this process: they tell the elevated hook
// which keystrokes to forward instead of letting them through. Win32 destroys the card together with the dialog
// that owns it, so the reset has to survive a manager with no cached window -- otherwise the flags stay true and
// every Enter/Backspace/IME key is swallowed in windows the hook does not recognize as text inputs.
[TestClass]
[DoNotParallelize] // InlineSearchManager is a process-wide singleton.
public sealed class InlineSearchManagerTests
{
    [TestMethod]
    public void CloseInlineSearch_WithNoCachedCard_ClearsTheHookFlags()
    {
        var manager = InlineSearchManager.Instance;
        manager.Window = null;
        manager.KeyboardHook.IsInlineSearchVisible = true;
        manager.KeyboardHook.IsInlineWindowOnScreen = true;

        manager.CloseInlineSearch("test: card destroyed by its host");

        Assert.IsFalse(manager.KeyboardHook.IsInlineSearchVisible,
            "a destroyed card must not leave the hook forwarding keystrokes to it");
        Assert.IsFalse(manager.KeyboardHook.IsInlineWindowOnScreen,
            "a destroyed card must not leave the hook treating it as on screen");
    }

    // Hiding a foreground window makes the OS hand the foreground to whatever comes next in z-order, which
    // is how dismissing the card left some other app's window in front of Explorer.
    [TestMethod]
    public void ShouldRestoreHostForeground_WhenTheCardHeldIt_HandsItBackToTheHost()
    {
        Assert.IsTrue(InlineSearchManager.ShouldRestoreHostForeground(
            new IntPtr(0x100), foregroundAtHide: new IntPtr(0x200), ownHwnd: new IntPtr(0x200)));
    }

    [TestMethod]
    public void ShouldRestoreHostForeground_WhenSomethingElseTookIt_LeavesItAlone()
    {
        // Closing because the user left Explorer, or clicked into another app: the card no longer holds the
        // foreground, so restoring would drag them back to where they were typing.
        Assert.IsFalse(InlineSearchManager.ShouldRestoreHostForeground(
            new IntPtr(0x100), foregroundAtHide: new IntPtr(0x300), ownHwnd: new IntPtr(0x200)));
    }

    [TestMethod]
    public void ShouldRestoreHostForeground_WithNoKnownHost_DoesNotRestore()
    {
        Assert.IsFalse(InlineSearchManager.ShouldRestoreHostForeground(
            IntPtr.Zero, foregroundAtHide: new IntPtr(0x200), ownHwnd: new IntPtr(0x200)));
        // A host that is the card itself has nothing to hand the foreground back to.
        Assert.IsFalse(InlineSearchManager.ShouldRestoreHostForeground(
            new IntPtr(0x200), foregroundAtHide: new IntPtr(0x200), ownHwnd: new IntPtr(0x200)));
    }
}
