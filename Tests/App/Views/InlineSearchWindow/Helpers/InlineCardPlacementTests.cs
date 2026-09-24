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
    public void ACardWithNoRoomBelowIsHungFromTheAnchoredWindowsTopEdge()
    {
        // A dialog's Open/Cancel row sits on its bottom edge, and a plain window's list scrolls from the top
        // down; attaching to the top edge is what keeps the row clear by arithmetic (AvailableCardHeight caps
        // the card at a share of the window) instead of an offset guessed to be one search box tall.
        var anchored = Between(Positioner(), "else if (tracker.ActiveHwnd != IntPtr.Zero)", "var targetLeft");

        Assert.Contains("targetPhysTop = hangsBelow", anchored,
            "one placement decision, both branches of it answering from the same rect");
        Assert.Contains(": rect.Top - physXamlMarginY", anchored, "and the no-room case attaches to the top edge");
        Assert.DoesNotContain("rect.Bottom - physWindowHeight", anchored,
            "not to the bottom edge, whose position a growing card moves");
    }

    [TestMethod]
    public void BothPlacementsShareOneHorizontalAnchor()
    {
        // The two answers used to be written out per placement, which is how a resize that turned the inside
        // placement into a drop-down ended up walking an Explorer card sideways under the user's own typing.
        var anchored = Between(Positioner(), "else if (tracker.ActiveHwnd != IntPtr.Zero)", "var minLeft");

        Assert.AreEqual(1, Count(anchored, "targetPhysLeft ="),
            "the horizontal anchor is decided once, not once per placement");
        Assert.Contains("rect.Right - physWindowWidth", anchored,
            "an Explorer window keeps its right-edge dock in both");
        Assert.Contains("isDialog", anchored, "while a dialog keeps its own centered-or-anchored answer");
    }

    [TestMethod]
    public void NothingIsRepositionedWhileNoWindowIsTracked()
    {
        // Deactivation leaves the mirror at ActiveHwnd=0/IsDesktop=false for up to 200ms while the card is
        // still visible (and startup starts there before the mirror's first state arrives). Neither
        // placement branch answers for that state, so without the guard the target keeps its (0,0)
        // initializer and the still-visible card is flung into the screen corner.
        var core = Between(Positioner(), "private void PositionWindowCore()", "var isResultsVisible");

        Assert.Contains("if (!tracker.IsDesktop && tracker.ActiveHwnd == IntPtr.Zero)", core,
            "the untracked state must be recognized");
        Assert.Contains("return;", core, "and it must keep the card where it already is");
    }

    private static int Count(string text, string needle) => text.Split(needle, StringSplitOptions.None).Length - 1;

    [TestMethod]
    public void TheCardIsClampedToTheWorkingAreaOnceForEveryPlacement()
    {
        // Three placements, one clamp. The separate per-mode clamps each re-derived the limit from a
        // different height (shell for one, visible card for the others), so the modes did not agree about
        // where the bottom of the screen was.
        // Three placements, one vertical clamp. The per-mode clamps each re-derived the limit from a
        // different height (the shell in one, the visible card in the others), so the modes did not agree
        // about where the bottom of the screen was.
        var clamp = Between(Positioner(), "targetPhysLeft = Math.Clamp(targetPhysLeft, minLeft, Math.Max(minLeft, maxLeft));", "var targetLeft");

        // The horizontal clamp above is the range's own opening line, so two clamps in it means no third.
        Assert.AreEqual(2, clamp.Split("Math.Clamp", StringSplitOptions.None).Length - 1,
            "one horizontal clamp and one vertical clamp, shared by every placement");
        Assert.Contains("minLeft, Math.Max(minLeft, maxLeft)", clamp,
            "the horizontal clamp must be guarded like the vertical one: a target window spanning two "
            + "monitors makes the card wider than one working area, and Math.Clamp throws when min > max");
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
