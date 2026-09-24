namespace Lertaro.Core.Tests.Hook;

// Which line a card hanging "below" its host starts at. The card's height budget and its placement both
// read this one answer, so it is settled here rather than in either of them: measured from two different
// lines, a card sized to hang outside a dialog gets drawn over it.
[TestClass]
public sealed class InlineCardHangTests
{
    private static Lertaro.Core.Hook.ExplorerTracker.RECT Box(int left, int top, int right, int bottom) =>
        new() { Left = left, Top = top, Right = right, Bottom = bottom };

    [TestMethod]
    public void TheButtonRowsTopEdgeIsWhereTheCardStarts()
    {
        // Measured off a live Save As: the dialog runs to 924, its file list ends at 788, and the row the
        // 保存 button sits in starts at 862 -- which is where the card hangs from, so it clears the address
        // bar and takes the space the dialog never uses.
        var hang = Lertaro.Core.Hook.ExplorerTracker.InlineCardHang.Resolve(
            Box(191, 321, 1048, 924), Box(192, 862, 1055, 931), Box(352, 438, 1047, 788));

        Assert.AreEqual(862, hang.BelowY);
        Assert.AreEqual(862, hang.ButtonRow?.Top);
        Assert.AreEqual(1047, hang.FileList?.Right);
        Assert.AreEqual(438, hang.FileList?.Top);
    }

    [TestMethod]
    public void WithNoButtonRowTheWindowKeepsItsOwnBottomEdge()
    {
        // A plain Explorer window, and every dialog whose adapter cannot see that far in (the classic file
        // dialog, the archive tools' pickers): the hang line stays where it has always been.
        var dock = Box(0, 0, 1000, 800);
        Assert.AreEqual(800, Lertaro.Core.Hook.ExplorerTracker.InlineCardHang.Resolve(dock, null, null).BelowY);

        // A file list on its own moves nothing below: it is the answer for lying over the window.
        var listOnly = Lertaro.Core.Hook.ExplorerTracker.InlineCardHang.Resolve(dock, null, Box(10, 20, 990, 700));
        Assert.AreEqual(800, listOnly.BelowY);
        Assert.IsNull(listOnly.ButtonRow);
    }

    [TestMethod]
    public void AnEmptyAnswerIsWhatAWindowWithoutInnerEdgesGets()
    {
        // The caller (ExplorerTracker.GetInlineCardHang) fills these from the active dialog's adapter; with
        // no adapter, or one that opts out, the answers stay null and every placement rung behind them is
        // the one that shipped before the dialog could name anything inside itself.
        var hang = Lertaro.Core.Hook.ExplorerTracker.InlineCardHang.Resolve(Box(0, 0, 1000, 800), null, null);

        Assert.IsNull(hang.ButtonRow);
        Assert.IsNull(hang.FileList);
    }
}
