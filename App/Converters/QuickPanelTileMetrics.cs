using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Lertaro.App.Converters;

/// <summary>How wide a quick panel tile is, and how big the picture inside it is, for a list this wide.</summary>
/// <remarks>
/// A fixed tile width left the panel's tiles the same size whatever it was docked to, so a wide window
/// got a row of ten small thumbnails with nothing gained from the space. The width is divided among at
/// most five instead, and every tile spends what it gets on the picture.
///
/// Five is a floor on the count, not a promise of it. Both ends give way, and for the same reason -- a
/// tile is only worth the width it can use:
///
///   - Narrow, where a fifth of the list would be smaller than the tiles used to be: the row wraps at
///     four, or three, which is better than five unreadable ones.
///   - Wide, where a fifth would be more than the picture can fill: the row takes a sixth, an eighth,
///     however many that width now holds. The alternative is five tiles each padded with the space it
///     could not use.
///
/// Whatever the count comes out at, the width is divided between them rather than handed out in fixed
/// lumps, so a row is never short of its own right edge. Smaller tiles are the better trade: an empty
/// strip at the end of every row is the one thing that reads as a mistake.
///
/// What the picture can fill is where the ceiling comes from: icons arrive at 256px from a thumbnail
/// provider and 96 from the shell's own path (see ShellImageListInterop), so past a point a bigger tile
/// is only stretching what it already has.
/// </remarks>
public sealed class QuickPanelTileMetrics : IValueConverter
{
    /// <summary>The most tiles a row is divided into, while they still have use for the width.</summary>
    public const int Columns = 5;

    /// <summary>Never smaller than the tile was before this existed.</summary>
    private const double MinSlot = 92;

    /// <summary>Where the picture stops being able to use more width.</summary>
    private const double MaxIcon = 160;

    // The slot's own border margin and padding, plus the breathing room around the picture inside it.
    private const double SlotChrome = 24;

    /// <summary>The widest a tile is ever made: any more would be padding, so it buys another tile.</summary>
    internal const double MaxSlot = MaxIcon + SlotChrome;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not double available || double.IsNaN(available) || available <= 0)
            return DependencyProperty.UnsetValue;

        var slot = SlotFor(available);

        // Leaving the name room underneath: the tile is the picture plus up to two lines of text, and a
        // picture that took the whole slot would push the name out of it.
        var iconWidth = Math.Max(48, slot - SlotChrome);

        return (parameter as string) switch
        {
            "Icon" => iconWidth,
            "IconHeight" => IconHeightFor(slot),
            "Cell" => CellHeightFor(slot),
            _ => slot,
        };
    }

    // Room under the picture for up to two lines of name, plus the tile's own padding.
    private const double TextRoom = 52;

    /// <summary>The height of a tile's picture box: wider than tall, and the same for every tile.</summary>
    /// <remarks>
    /// A box, not the picture's own height. Letting each picture keep its natural height made every row
    /// as tall as its tallest member, so a folder of mixed content came out ragged: a square icon set the
    /// row height and the thumbnails beside it floated in the middle of it.
    ///
    /// Landscape rather than square, because a square box is what put a band of empty tile above and
    /// below every 16:9 thumbnail, and video is what a folder of thumbnails usually is. At two thirds of
    /// the width a 16:9 picture very nearly fills it, and a square icon simply scales down to fit the
    /// height instead of stretching the row: both keep their own shape, and the grid stays a grid.
    /// </remarks>
    internal static double IconHeightFor(double slotWidth)
        => Math.Floor(Math.Max(48, slotWidth - SlotChrome) * 2 / 3);

    internal static double CellHeightFor(double slotWidth) => IconHeightFor(slotWidth) + TextRoom;

    /// <summary>How wide each tile is, for a list this wide.</summary>
    /// <remarks>
    /// The count is worked out from the width rather than the other way round: take the fewest columns
    /// that keeps a tile within <see cref="MaxSlot"/>, never fewer than <see cref="Columns"/>, then
    /// divide the width evenly between them. Dividing is the point -- handing out a fixed size instead
    /// leaves whatever did not divide evenly as a gap at the end of every row, which is worse than
    /// tiles a few pixels smaller.
    ///
    /// The floor wins over all of it: below <see cref="MinSlot"/> a tile stops being worth looking at,
    /// so a panel too narrow for five of those takes four, or two, and divides the width between those.
    /// </remarks>
    internal static double SlotFor(double available)
    {
        var columns = ColumnsFor(available);

        // Floored, so the columns can never come to a hair more than the width they were divided from --
        // which a wrap panel answers by dropping one of them onto the next row.
        return Math.Floor(available / columns);
    }

    internal static int ColumnsFor(double available)
    {
        if (available <= 0 || double.IsNaN(available)) return 1;

        var columns = Math.Max(Columns, (int)Math.Ceiling(available / MaxSlot));
        var mostThatFit = (int)Math.Floor(available / MinSlot);
        return mostThatFit < columns ? Math.Max(1, mostThatFit) : columns;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
