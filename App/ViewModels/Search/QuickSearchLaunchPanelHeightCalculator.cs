namespace Lertaro.App.ViewModels.Search;

internal static class QuickSearchLaunchPanelHeightCalculator
{
    // The item slot includes the launch card's 2-DIP margin plus the selection overlay's 2-DIP
    // margin and padding on both vertical sides. The old estimate counted only the card margin,
    // making the calculated panel height smaller than the rendered UniformGrid rows and showing a
    // scrollbar even when all items technically fit.
    private const double LaunchItemSlotHeight = 104;
    // Keep a small bottom buffer for the ListView's content presenter so an exactly fitting last row
    // does not become a fractional overflow and reveal the overlay scrollbar.
    private const double ItemsVerticalPadding = 16;
    private const double SourceTabsHeight = 38;
    private const double SourceTabsBottomMargin = 2;

    public static double Calculate(IReadOnlyCollection<LaunchPanelSourceViewModel> sources, int columns,
        double maximumHeight)
    {
        if (sources.Count == 0 || columns <= 0 || maximumHeight <= 0) return 0;

        var maximumItemCount = sources.Max(source => source.Items.Count);
        var rows = Math.Max(1, (maximumItemCount + columns - 1) / columns);
        var itemsHeight = rows * LaunchItemSlotHeight + ItemsVerticalPadding;
        var tabsHeight = sources.Count > 1 ? SourceTabsHeight + SourceTabsBottomMargin : 0;
        return Math.Min(maximumHeight, itemsHeight + tabsHeight);
    }
}
