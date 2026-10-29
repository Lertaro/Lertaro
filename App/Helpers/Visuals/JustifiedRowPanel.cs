// This project sets UseWindowsForms, whose global usings make Panel and Size ambiguous.
using Panel = System.Windows.Controls.Panel;
using Size = System.Windows.Size;
using Rect = System.Windows.Rect;
using UIElement = System.Windows.UIElement;

namespace Lertaro.App.Helpers.Visuals;

/// <summary>
/// Lays its children out in one row at their own widths, then spreads the leftover space between them so the
/// first starts at the left edge and the last ends at the right one.
/// </summary>
/// <remarks>
/// The equal-width alternative (<see cref="UniformGrid"/>) is what the filter bar used, and it is the wrong
/// shape for labels: every cell becomes as wide as the longest word, so a two-character 全部 sits in a field
/// sized for Aplicaciones, and a locale with longer words than the space simply clips. Here each label takes
/// the width it needs and the gaps carry the rest.
///
/// Ask for it inside a ScrollViewer whose horizontal bar is Auto: measured against infinite width this panel
/// reports exactly the sum of its children, so the bar appears the moment that sum exceeds the viewport, and
/// when it does not, WPF arranges the content at the viewport width -- which is the leftover this panel
/// distributes. Justify and scroll are therefore the same rule, not two modes: too many tabs stop being a
/// layout failure and become a scrollable row.
/// </remarks>
public class JustifiedRowPanel : Panel
{
    protected override Size MeasureOverride(Size availableSize)
    {
        // Infinite width, always: a child measured against a share of the available space would trim or wrap
        // and report a width that is not the one its text needs.
        double width = 0;
        double height = 0;
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new Size(double.PositiveInfinity, availableSize.Height));
            width += child.DesiredSize.Width;
            height = Math.Max(height, child.DesiredSize.Height);
        }

        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var count = InternalChildren.Count;
        if (count == 0)
            return finalSize;

        var widths = new double[count];
        double total = 0;
        for (var i = 0; i < count; i++)
        {
            widths[i] = InternalChildren[i].DesiredSize.Width;
            total += widths[i];
        }

        // No room left over (the row is as wide as its content, or wider and now scrolling) means no gaps: the
        // only way a negative gap could be read is as overlap.
        //
        // The leftover is split per CHILD, not per gap. Every child's box then grows by one gap, its content
        // (centred) leaves exactly half a gap of empty space on either side, and the boxes tile the row end to
        // end -- so all the gaps come out equal and the row still spans exactly finalSize.
        //
        // Splitting per gap instead (the previous rule) put the whole gap outside each child's box, and that is
        // the shape the filter bar's pipe exposed: the pipe is drawn at the child's own left edge, so with no
        // empty space inside the box it sat hard against the previous label with all the space on its right.
        var gap = count > 0 ? Math.Max(0, (finalSize.Width - total) / count) : 0;

        var x = 0.0;
        for (var i = 0; i < count; i++)
        {
            var width = widths[i] + gap;
            InternalChildren[i].Arrange(new Rect(x, 0, width, finalSize.Height));
            x += width;
        }

        return finalSize;
    }
}
