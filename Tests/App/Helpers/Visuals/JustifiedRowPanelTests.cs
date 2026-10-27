using Lertaro.App.Helpers.Visuals;
// Tests/App sets UseWindowsForms too, and these are the names both toolkits spell the same way.
using Control = System.Windows.Controls.Control;
using Panel = System.Windows.Controls.Panel;
using Point = System.Windows.Point;
using Rect = System.Windows.Rect;
using Size = System.Windows.Size;

namespace Lertaro.App.Tests.Helpers.Visuals;

// The filter bar's layout: every tab at its own text width, the leftover space carried by the gaps, and no
// gaps at all once the row is wider than the space it was given (which is when the bar starts scrolling).
[TestClass]
public sealed class JustifiedRowPanelTests
{
    private static (Panel Panel, List<Control> Cells) Build(params double[] widths)
    {
        var panel = new JustifiedRowPanel();
        var cells = new List<Control>();
        foreach (var width in widths)
        {
            var cell = new Control { Width = width, Height = 16 };
            cells.Add(cell);
            panel.Children.Add(cell);
        }

        return (panel, cells);
    }

    // Where a cell ended up, read the way WPF exposes it without depending on an internal property.
    private static Rect BoundsOf(Control cell, Panel panel)
    {
        var at = cell.TranslatePoint(new Point(), panel);
        return new Rect(at.X, at.Y, cell.ActualWidth, cell.ActualHeight);
    }

    [StaTestMethod]
    public void EveryCell_TakesItsOwnWidth_AndTheGapsCarryWhatIsLeft()
    {
        var (panel, cells) = Build(40, 60, 50);

        panel.Measure(new Size(300, 20));
        // Asked for a share of 300 the cells would have to shrink; measured at infinity they report what they
        // need, which is what makes the row's total the number the viewport is compared against.
        Assert.AreEqual(150, panel.DesiredSize.Width, 0.01);

        panel.Arrange(new Rect(0, 0, 300, 20));

        // (300 - 150) / (3 - 1): the first tab starts on the left edge and the last ends on the right one.
        Assert.AreEqual(0, BoundsOf(cells[0], panel).X, 0.01);
        Assert.AreEqual(40, BoundsOf(cells[0], panel).Width, 0.01);
        Assert.AreEqual(115, BoundsOf(cells[1], panel).X, 0.01);
        Assert.AreEqual(250, BoundsOf(cells[2], panel).X, 0.01);
        Assert.AreEqual(300, BoundsOf(cells[2], panel).Right, 0.01);
    }

    [StaTestMethod]
    public void ARowWithNoRoomLeft_PacksFromTheLeftWithoutOverlap()
    {
        // The overflowing case, which is the scrolling case: a negative gap could only be read as overlap, so
        // the row stays exactly as wide as its tabs and the ScrollViewer takes it from there.
        var (panel, cells) = Build(120, 120, 120);

        // The ScrollViewer measures its content at infinite width, which is how it learns the extent at all:
        // a finite constraint would clamp the reported width to itself and the bar would never appear.
        panel.Measure(new Size(double.PositiveInfinity, 20));
        Assert.AreEqual(360, panel.DesiredSize.Width, 0.01);

        panel.Arrange(new Rect(0, 0, 360, 20));

        Assert.AreEqual(0, BoundsOf(cells[0], panel).X, 0.01);
        Assert.AreEqual(120, BoundsOf(cells[1], panel).X, 0.01);
        Assert.AreEqual(240, BoundsOf(cells[2], panel).X, 0.01);
        Assert.AreEqual(360, BoundsOf(cells[2], panel).Right, 0.01);
    }

    [StaTestMethod]
    public void ArrangedNarrowerThanItsContent_TheRowStaysWholeAndStartsAtZero()
    {
        // The extent is wider than the viewport's minimum here, so nothing may squeeze the cells: the row is
        // laid out at full width and the horizontal bar is what reaches the rest.
        var (panel, cells) = Build(120, 120, 120);

        panel.Measure(new Size(double.PositiveInfinity, 20));
        panel.Arrange(new Rect(0, 0, 200, 20));

        Assert.AreEqual(0, BoundsOf(cells[0], panel).X, 0.01);
        Assert.AreEqual(240, BoundsOf(cells[2], panel).X, 0.01);
    }

    [StaTestMethod]
    public void ASingleTab_IsLeftAlignedAndTakesNoGap()
    {
        // A plugin list reduced to the host's own 全部 tab must not be centred, and must not divide by the
        // zero gaps a one-item row has.
        var (panel, cells) = Build(40);

        panel.Measure(new Size(300, 20));
        panel.Arrange(new Rect(0, 0, 300, 20));

        Assert.AreEqual(0, BoundsOf(cells[0], panel).X, 0.01);
        Assert.AreEqual(40, BoundsOf(cells[0], panel).Width, 0.01);
    }

    [StaTestMethod]
    public void AnEmptyRow_ArrangesNothingAndDoesNotThrow()
    {
        var (panel, _) = Build();

        panel.Measure(new Size(300, 20));
        panel.Arrange(new Rect(0, 0, 300, 20));

        Assert.AreEqual(0, panel.DesiredSize.Width, 0.01);
    }
}
