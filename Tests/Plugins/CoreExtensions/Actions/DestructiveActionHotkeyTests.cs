using Lertaro.Plugins.CoreExtensions.Actions;

namespace Lertaro.Plugins.CoreExtensions.Tests.Actions;

// The four actions that move or destroy files ship with no hotkey bound. They used to carry Explorer's
// own keys (Ctrl+X, Ctrl+V, Delete, Shift+Delete), which reads as obvious until you remember where they
// sit: under a search box, where Delete means "delete a character" and Ctrl+V means "paste text". Users
// reported firing them by accident, and Shift+Delete is the one mistake in this set that nothing can
// undo. All four are still in the actions menu and still bindable in Settings -- this only stops them
// being inherited by someone who never asked for them.
[TestClass]
public sealed class DestructiveActionHotkeyTests
{
    [TestMethod]
    public void DestructiveActions_HaveNoDefaultHotkey()
    {
        Assert.IsEmpty(new CutFileAction().Hotkey);
        Assert.IsEmpty(new PasteFileAction().Hotkey);
        Assert.IsEmpty(new DeleteFileAction().Hotkey);
        Assert.IsEmpty(new PermanentDeleteFileAction().Hotkey);
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
