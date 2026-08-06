using Lertaro.App.Converters;

namespace Lertaro.App.Views.QuickPanel;

// Shared by the two panel-owned gutters. Keeping the count arithmetic here makes the WPF layout code
// only responsible for measuring and arranging visuals.
internal static class QuickPanelLineNumberLayout
{
    public static int DigitsFor(int largestNumber)
        => Math.Max(1, Math.Max(1, largestNumber).ToString(System.Globalization.CultureInfo.CurrentCulture).Length);

    public static int RowsFor(int itemCount, int columns)
        => itemCount <= 0 ? 0 : (itemCount + Math.Max(1, columns) - 1) / Math.Max(1, columns);

    public static int ThumbnailColumnsFor(double availableWidth, double gutterWidth)
    {
        var contentWidth = Math.Max(0, availableWidth - gutterWidth);
        return QuickPanelTileMetrics.ColumnsFor(contentWidth);
    }
}
