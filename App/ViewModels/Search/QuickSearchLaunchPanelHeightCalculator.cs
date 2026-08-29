namespace Lertaro.App.ViewModels.Search;

internal static class QuickSearchLaunchPanelHeightCalculator
{
    private const double LaunchItemHeight = 92;
    private const double LaunchItemVerticalMargin = 4;
    private const double ItemsVerticalPadding = 12;
    private const double SourceTabsHeight = 38;
    private const double SourceTabsBottomMargin = 2;

    public static double Calculate(IReadOnlyCollection<LaunchPanelSourceViewModel> sources, int columns,
        double maximumHeight)
    {
        if (sources.Count == 0 || columns <= 0 || maximumHeight <= 0) return 0;

        var maximumItemCount = sources.Max(source => source.Items.Count);
        var rows = Math.Max(1, (maximumItemCount + columns - 1) / columns);
        var itemsHeight = rows * (LaunchItemHeight + LaunchItemVerticalMargin) + ItemsVerticalPadding;
        var tabsHeight = sources.Count > 1 ? SourceTabsHeight + SourceTabsBottomMargin : 0;
        return Math.Min(maximumHeight, itemsHeight + tabsHeight);
    }
}
