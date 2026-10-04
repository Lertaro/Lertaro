using System.Globalization;
using System.Windows;
using System.Windows.Data;
using Lertaro.Core;

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
/// is only stretching what it already has. How high that ceiling sits is the user's call, as
/// <see cref="IconSize"/>: Small draws the picture at two fifths of ExtraLarge's width, Medium at a bit
/// over half and Large at three quarters, with ExtraLarge -- the size the tiles were before any of this
/// was adjustable -- unchanged in every number.
/// </remarks>
public sealed class QuickPanelTileMetrics : IValueConverter
{
    /// <summary>The most tiles a row is divided into, while they still have use for the width.</summary>
    public const int Columns = 5;

    /// <summary>
    /// The size the panel's tiles draw at, set from the settings on every open.
    /// </summary>
    /// <remarks>
    /// Ambient rather than bound through three layers that would each only carry it: the converter's
    /// bindings, the wrap panel's layout math and the gutter's column count all answer to one value,
    /// and everything that reads it is rebuilt with the window on every open. The manager sets this
    /// before that window exists, so nothing on screen is ever sized from a stale one.
    /// </remarks>
    public static QuickPanelThumbnailSize IconSize { get; set; } = QuickPanelThumbnailSize.ExtraLarge;

    // Per size: the floor below which a tile stops being worth looking at, and the widest picture one
    // draws -- the slot's ceiling follows as the picture plus its chrome. Small, Medium and Large floor
    // at the picture's own 48px minimum plus the chrome, since a size that never went below the tiles'
    // old fixed width would not be one; ExtraLarge keeps that old fixed size as its floor, and its
    // ceiling is where it always stopped being able to use more width.
    //
    // Both then move with the Ctrl+wheel multiplier, which is the same rule applied to a tile the user
    // has asked to be finer or coarser: the floor is where a tile stays readable, and the ceiling is
    // where the picture stops being able to fill what it is given. Scaling both is what makes the wheel
    // fit more tiles in the same window rather than leaving each one in a cell it no longer fills.
    // Scaling only one would let a shrunken tile be measured as though it were still full size.
    private static double MinSlot => (IconSize == QuickPanelThumbnailSize.ExtraLarge ? 92 : 72) * IconScale;

    /// <summary>Where the picture stops being able to use more width.</summary>
    private static double MaxIcon => (IconSize switch
    {
        QuickPanelThumbnailSize.Small => 64,
        QuickPanelThumbnailSize.Medium => 88,
        QuickPanelThumbnailSize.Large => 120,
        _ => 160,
    }) * IconScale;

    // The slot's own border margin and padding, plus the breathing room around the picture inside it.
    private const double SlotChrome = 24;

    /// <summary>The Ctrl+wheel multiplier in force, read fresh so the panel's re-measure sees the new one.</summary>
    /// <remarks>
    /// Read from the settings rather than held beside <see cref="IconSize"/>, because the two are set by
    /// different surfaces: the tier comes from the settings page before the window is built, while this
    /// changes under the pointer. Read here, one wheel step lands on the next measure with nothing to
    /// keep in step.
    /// </remarks>
    private static double IconScale => UserSettings.Load().QuickPanel.EffectiveIconScale;

    /// <summary>The widest a tile is ever made: any more would be padding, so it buys another tile.</summary>
    internal static double MaxSlot => MaxIcon + SlotChrome;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not double available || double.IsNaN(available) || available <= 0)
            return DependencyProperty.UnsetValue;

        var slot = SlotFor(available);
        var iconWidth = IconWidthFor(slot);

        return (parameter as string) switch
        {
            "Icon" => iconWidth,
            "IconHeight" => IconHeightFor(slot),
            "Cell" => CellHeightFor(slot),
            _ => slot,
        };
    }

    /// <summary>How wide the picture inside a slot is: the slot less its chrome, floored at the picture's own minimum.</summary>
    /// <remarks>
    /// Shared rather than written out at each call site: the tile template reaches it through the
    /// converter above, and the wrap panel publishes the same number for the picture to bind to, so the
    /// two have to agree on it or a tile's box and the cell it sits in would disagree about the chrome.
    /// </remarks>
    internal static double IconWidthFor(double slotWidth) => Math.Max(48, slotWidth - SlotChrome);

    // Room under the picture for the name, plus the tile's own padding.
    //
    // Sized for ONE line, which is what the tile template actually draws: its TextBlock has no
    // TextWrapping, so a long name is trimmed by the marquee rather than wrapped onto a second line. This
    // was 52, the room two lines would have taken, and the difference showed up as a blank strip under
    // every tile -- a row looked taller than its contents for no reason the user could see.
    //
    // The figure covers the gap above the name (the template's margin), one 11pt line, and the tile's own
    // padding. Raising it back is only right if the template starts wrapping, in which case it needs the
    // second line back rather than a few pixels more.
    private const double TextRoom = 37;

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
        => Math.Floor(IconWidthFor(slotWidth) * 2 / 3);

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
