using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace Lertaro.App.Tests.Views.Controls;

// A result list of tens of thousands of rows used to leave the overlay scrollbar's thumb under a pixel: the
// bar is visible, but there is nothing to click and drag. The floor is one resource on the template's Track,
// which Track reads as twice the thumb's minimum height, and it is worth pinning because the obvious
// alternative fails quietly: setting Thumb.MinHeight by hand enlarges the thumb while Track goes on
// positioning it with the proportional length, so the extra height hangs past the end of the track.
[TestClass]
public sealed class ScrollBarThumbFloorTests
{
    private const double BarHeight = 600;
    private const double Minimum = 20;

    // 6000 units of extent with 100 of them visible, i.e. Track's own thumb length would be 10px.
    private const double HugeMaximum = 5900;

    [StaTestMethod]
    public void Thumb_IsFlooredInsideTheTrackAcrossTheWholeRange()
    {
        var style = ShippedScrollBarStyle();

        foreach (var value in new[] { 0d, HugeMaximum / 2, HugeMaximum })
        {
            var (top, height) = Layout(style, HugeMaximum, 100, value);
            Assert.IsGreaterThanOrEqualTo(Minimum, height, $"value {value}: the thumb has to stay grabbable");
            Assert.IsGreaterThanOrEqualTo(0d, top, $"value {value}: and a floored thumb must not hang above the track");
            Assert.IsLessThanOrEqualTo(BarHeight, top + height, $"value {value}: nor past its bottom");
        }

        Assert.AreEqual(0d, Layout(style, HugeMaximum, 100, 0).Top, 0.5, "scrolled to the top it must sit on the top edge");
        var atTheEnd = Layout(style, HugeMaximum, 100, HugeMaximum);
        Assert.AreEqual(BarHeight, atTheEnd.Top + atTheEnd.Height, 0.5, "and scrolled to the bottom, on the bottom edge");

        // A floor, not a fixed size: an ordinary list keeps the proportional thumb it already had.
        var (plainTop, plainHeight) = Layout(style, 600, 600, 300);
        Assert.AreEqual(300, plainHeight, 0.5);
        Assert.AreEqual(150, plainTop, 0.5);
    }

    // WPF's pack scheme is registered by the Application type, and these tests run without one.
    public ScrollBarThumbFloorTests() => _ = Application.ResourceAssembly;

    private static Style ShippedScrollBarStyle()
    {
        var resources = new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/Lertaro.App;component/Resources/Styles/Controls/ScrollBar.xaml", UriKind.Absolute),
        };
        return Assert.IsInstanceOfType<Style>(resources[typeof(ScrollBar)]);
    }

    private static (double Top, double Height) Layout(Style style, double maximum, double viewport, double value)
    {
        var bar = new ScrollBar
        {
            Style = style,
            Orientation = Orientation.Vertical,
            Minimum = 0,
            Maximum = maximum,
            ViewportSize = viewport,
            Value = value,
        };
        bar.Measure(new Size(6, BarHeight));
        bar.Arrange(new Rect(0, 0, 6, BarHeight));

        var thumb = Assert.IsInstanceOfType<Thumb>(bar.Template.FindName("Thumb", bar));
        var track = Assert.IsInstanceOfType<Track>(bar.Template.FindName("PART_Track", bar));
        return (thumb.TranslatePoint(new Point(0, 0), track).Y, thumb.ActualHeight);
    }
}
