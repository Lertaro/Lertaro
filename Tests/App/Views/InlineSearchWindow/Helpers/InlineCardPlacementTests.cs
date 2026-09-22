using System.IO;

namespace Lertaro.App.Tests.Views.InlineSearchWindow.Helpers;

// Guards on the inline card's placement. The positioner needs a live window and a real desktop to do
// anything, so none of this is assertable as behaviour; each guard is a decision the geometry has to keep
// making the same way, and the reasoning for it lives in InlineSearchWindowPositioner's own comments.
[TestClass]
public sealed class InlineCardPlacementTests
{
    [TestMethod]
    public void RoomBelowIsAskedOfEveryAnchoredWindowNotJustDialogs()
    {
        // The card's height budget has always come from this same spaceBelow measurement
        // (InlineCardMetrics.AvailableCardHeight). If only dialogs act on it, a plain Explorer window gets a
        // height sized to hang outside and is then drawn inside anyway -- which is what made the card cover
        // the file list it had room to sit below.
        var gate = Between(Positioner(), "var hangsBelow = false;", "var dropDown =");

        Assert.DoesNotContain("Dialog", gate,
            "the room-below gate must ask the same question of an Explorer window and a file dialog");
        Assert.Contains("spaceBelow", gate, "and it must still be the space-below measurement");
    }

    [TestMethod]
    public void ADialogWithNoRoomBelowIsHungFromItsTopEdge()
    {
        // A dialog's Open/Cancel row sits on its bottom edge. Attaching the card to the top instead means
        // AnchoredWindowHeightShare decides whether that row is covered, rather than an offset guessed to be
        // one search box tall.
        var inside = Between(Positioner(), "else if (isDialog)", "targetPhysTop = rect.Bottom - physWindowHeight");

        Assert.Contains("targetPhysTop = rect.Top", inside, "the card must attach to the dialog's top edge");
        Assert.DoesNotContain("rect.Bottom", inside,
            "and must not be placed off its bottom edge, which is where the buttons are");
    }

    [TestMethod]
    public void AnExplorerCardKeepsItsRightDockWhenItHangsBelow()
    {
        // Both Explorer placements have to share one horizontal anchor, or resizing a window until the room
        // below runs out walks the card sideways under the user's own typing.
        var below = Between(Positioner(), "if (hangsBelow)", "else if (isDialog)");

        Assert.Contains("rect.Right - physWindowWidth", below,
            "the drop-down keeps the inside placement's right-edge dock");
        Assert.Contains("isDialog", below, "while a dialog's own drop-down stays centered under it");
    }

    [TestMethod]
    public void TheCardIsClampedToTheWorkingAreaOnceForEveryPlacement()
    {
        // Three placements, one clamp. The separate per-mode clamps each re-derived the limit from a
        // different height (shell for one, visible card for the others), so the modes did not agree about
        // where the bottom of the screen was.
        // Three placements, one vertical clamp. The per-mode clamps each re-derived the limit from a
        // different height (the shell in one, the visible card in the others), so the modes did not agree
        // about where the bottom of the screen was.
        var clamp = Between(Positioner(), "targetPhysLeft = Math.Clamp(targetPhysLeft, minLeft, maxLeft);", "var targetLeft");

        // The horizontal clamp above is the range's own opening line, so two clamps in it means no third.
        Assert.AreEqual(2, clamp.Split("Math.Clamp", StringSplitOptions.None).Length - 1,
            "one horizontal clamp and one vertical clamp, shared by every placement");
        Assert.Contains("minTop, Math.Max(minTop, maxTop)", clamp,
            "and the vertical one must still be able to pull the card back onto the screen");
    }

    private static string Positioner() =>
        Source("App/Views/InlineSearchWindow/Helpers/InlineSearchWindowPositioner.cs");

    private static string Between(string source, string from, string to)
    {
        var start = source.IndexOf(from, StringComparison.Ordinal);
        Assert.IsGreaterThan(-1, start, $"could not find '{from}'");
        var end = source.IndexOf(to, start + from.Length, StringComparison.Ordinal);
        Assert.IsGreaterThan(-1, end, $"could not find '{to}' after '{from}'");
        return source[start..end];
    }

    // Newline-normalized so a marker can name a line ending without caring what the checkout used.
    private static string Source(string relativePath) =>
        File.ReadAllText(Path.Combine(RepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar)))
            .Replace("\r\n", "\n");

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AGENTS.md")))
            dir = dir.Parent;
        Assert.IsNotNull(dir, "could not locate the repository root");
        return dir!.FullName;
    }
}
