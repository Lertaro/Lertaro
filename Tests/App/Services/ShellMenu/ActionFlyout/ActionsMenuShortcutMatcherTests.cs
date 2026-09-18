using Lertaro.App.Services.ShellMenu.ActionFlyout;

namespace Lertaro.App.Tests.Services.ShellMenu.ActionFlyout;

// Which menu row a typed letter runs.
//
// Requested behaviour: once the menu is open, p/h/c/n/m/r/f run the window actions without arrowing to
// them. The lookup is deliberately this small so the parts that can go wrong are all pinned here: the
// case a provider wrote, the rows a click could never run, and the hints that are hotkeys, not mnemonics.
[TestClass]
public sealed class ActionsMenuShortcutMatcherTests
{
    private static ActionMenuItem Item(string text, string hint, bool disabled = false) =>
        new() { Text = text, ShortcutHint = hint, IsDisabled = disabled };

    [TestMethod]
    public void Find_MatchingHint_ReturnsThatItem()
    {
        var items = new[] { Item("Maximize", "p"), Item("Close", "c") };

        Assert.AreEqual("Close", ActionsMenuShortcutMatcher.Find(items, 'c')!.Text);
    }

    // The provider may write either case and the letter arrives uppercase from the key: both directions
    // have to line up, or a spec written as "P" would only work while Caps Lock happened to be off.
    [TestMethod]
    public void Find_IsCaseInsensitive()
    {
        Assert.IsNotNull(ActionsMenuShortcutMatcher.Find(new[] { Item("Topmost", "p") }, 'P'));
        Assert.IsNotNull(ActionsMenuShortcutMatcher.Find(new[] { Item("Topmost", "P") }, 'p'));
    }

    // Headers, separators and disabled rows are visible but not runnable; a letter must skip them. This
    // matters for the window menu in particular: "maximize" is disabled on an already-maximized window,
    // and pressing its letter must not run it (nor run a header that happens to share the letter).
    [TestMethod]
    public void Find_SkipsRowsThatCannotBeRun()
    {
        var items = new[]
        {
            new ActionMenuItem { Text = "Group", IsSectionHeader = true, ShortcutHint = "m" },
            new ActionMenuItem { IsSeparator = true, ShortcutHint = "m" },
            Item("Maximize", "m", disabled: true),
            Item("Minimize", "m")
        };

        Assert.AreEqual("Minimize", ActionsMenuShortcutMatcher.Find(items, 'm')!.Text);
    }

    // A multi-character hint is a displayed hotkey ("Ctrl+O"), not a mnemonic -- matching its last letter
    // would make 'o' run an unrelated row.
    [TestMethod]
    public void Find_IgnoresMultiCharacterHints() => Assert.IsNull(ActionsMenuShortcutMatcher.Find(new[] { Item("Open", "Ctrl+O") }, 'o'));

    [TestMethod]
    public void Find_LetterNoItemClaims_ReturnsNull()
    {
        Assert.IsNull(ActionsMenuShortcutMatcher.Find(new[] { Item("Close", "c") }, 'z'));
        Assert.IsNull(ActionsMenuShortcutMatcher.Find([], 'c'));
    }
}
