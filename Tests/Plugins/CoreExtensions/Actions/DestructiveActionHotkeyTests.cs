using Lertaro.Plugins.CoreExtensions.Actions;

namespace Lertaro.Plugins.CoreExtensions.Tests.Actions;

// The four actions that move or destroy files carry Explorer's own keys (Ctrl+X, Ctrl+V, Delete,
// Shift+Delete). That reads as obvious until you remember where they sit -- under a search box, where
// Delete means "delete a character" and Ctrl+V means "paste text" -- so they are only offered while the
// box has nothing selected (see SearchInputHelper.TryActionHotkey), and the native prompts behind delete
// stay as the backstop. This test pins the defaults because dropping one is a user-visible change with a
// documented rationale on both sides of it, not an internal detail.
[TestClass]
public sealed class DestructiveActionHotkeyTests
{
    [TestMethod]
    public void DestructiveActions_KeepTheirExplorerDefaults()
    {
        Assert.AreEqual("Ctrl+X", new CutFileAction().Hotkey);
        Assert.AreEqual("Ctrl+V", new PasteFileAction().Hotkey);
        Assert.AreEqual("Delete", new DeleteFileAction().Hotkey);
        Assert.AreEqual("Shift+Delete", new PermanentDeleteFileAction().Hotkey);
    }

    // The non-destructive ones keep theirs: this is about what a key does, not about clearing defaults
    // wholesale, and losing Ctrl+Enter would be its own regression.
    [TestMethod]
    public void NonDestructiveActions_KeepTheirDefaults()
    {
        Assert.AreEqual("Ctrl+C", new CopyFileAction().Hotkey);
        Assert.AreEqual("Shift+C", new CopyNameAction().Hotkey);
        Assert.AreEqual("Ctrl+Shift+C", new CopyPathAction().Hotkey);
        Assert.AreEqual("Ctrl+Enter", new LocateInExplorerAction().Hotkey);
    }
}
