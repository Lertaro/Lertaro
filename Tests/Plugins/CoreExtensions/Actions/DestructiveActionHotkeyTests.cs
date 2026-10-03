using Lertaro.Plugins.CoreExtensions.Actions;

namespace Lertaro.Plugins.CoreExtensions.Tests.Actions;

// The four actions that move or destroy files carry Explorer's own keys: Ctrl+X, Ctrl+V, Delete and
// Shift+Delete. They were blanked for a while after accidental-firing reports, and putting them back is safe
// because of guards that live in the host, not here -- see SearchInputHelper.TryActionHotkey: a chord only
// reaches an action when the box has no text selected, a bare key only when the caret is already at the end
// of the query, and both deletes go through IFileOperation without FOF_NOCONFIRMATION, so the native prompt
// still stands between the key and the files. What this pins is the inherited default: someone who never
// opened Settings gets exactly these four keys, and any change to them is a user-visible decision, not a
// refactor side effect.
[TestClass]
public sealed class DestructiveActionHotkeyTests
{
    [TestMethod]
    public void DestructiveActions_CarryExplorersDefaults()
    {
        Assert.AreEqual("Ctrl+X", new CutFileAction().Hotkey);
        Assert.AreEqual("Ctrl+V", new PasteFileAction().Hotkey);
        Assert.AreEqual("Delete", new DeleteFileAction().Hotkey);
        Assert.AreEqual("Shift+Delete", new PermanentDeleteFileAction().Hotkey);
    }

    // The non-destructive ones keep theirs too: this file is about what a key does, and losing Ctrl+Enter
    // would be its own regression.
    [TestMethod]
    public void NonDestructiveActions_KeepTheirDefaults()
    {
        Assert.AreEqual("Ctrl+C", new CopyFileAction().Hotkey);
        Assert.AreEqual("Shift+C", new CopyNameAction().Hotkey);
        Assert.AreEqual("Ctrl+Shift+C", new CopyPathAction().Hotkey);
        Assert.AreEqual("Ctrl+Enter", new LocateInExplorerAction().Hotkey);
    }
}
