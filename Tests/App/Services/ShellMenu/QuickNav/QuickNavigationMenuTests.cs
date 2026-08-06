using System.Windows.Controls;
using Lertaro.App.Services.ShellMenu.QuickNav;

namespace Lertaro.App.Tests.Services.ShellMenu.QuickNav;

[TestClass]
public sealed class QuickNavigationMenuTests
{
    [StaTestMethod]
    public void FindVisualParent_SelfIsMatchingType_ReturnsSelf()
    {
        var grid = new Grid();

        Assert.AreSame(grid, QuickNavigationMenu.FindVisualParent<Grid>(grid));
    }

    [StaTestMethod]
    public void FindVisualParent_LogicalParentMatches_ReturnsParent()
    {
        // MenuItem is a FrameworkContentElement-free visual FrameworkElement whose logical parent is set
        // via AddLogicalChild; VisualTreeHelper only walks the *visual* tree, but Grid.Children.Add wires
        // both the visual and logical parent for a UIElement, which is what the walk actually needs here.
        var grid = new Grid();
        var child = new TextBlock();
        grid.Children.Add(child);

        Assert.AreSame(grid, QuickNavigationMenu.FindVisualParent<Grid>(child));
    }

    [StaTestMethod]
    public void FindVisualParent_NoMatchingAncestor_ReturnsNull()
    {
        var grid = new Grid();
        var child = new TextBlock();
        grid.Children.Add(child);

        Assert.IsNull(QuickNavigationMenu.FindVisualParent<Button>(child));
    }

    [StaTestMethod]
    public void FindVisualParent_NullChild_ReturnsNull() =>
        Assert.IsNull(QuickNavigationMenu.FindVisualParent<Grid>(null));

    [StaTestMethod]
    public void FindVisualParent_MultipleLevels_WalksPastNonMatchingAncestor()
    {
        // The middle ancestor (StackPanel) is deliberately not a Grid, proving the walk climbs past a
        // non-matching level rather than only ever checking the immediate parent.
        var outer = new Grid();
        var middle = new StackPanel();
        var leaf = new TextBlock();
        outer.Children.Add(middle);
        middle.Children.Add(leaf);

        Assert.AreSame(outer, QuickNavigationMenu.FindVisualParent<Grid>(leaf));
    }

    [StaTestMethod]
    public void FindVisualParent_NearestMatchingAncestor_ReturnsNearestNotOutermost()
    {
        var outer = new Grid();
        var inner = new Grid();
        var leaf = new TextBlock();
        outer.Children.Add(inner);
        inner.Children.Add(leaf);

        Assert.AreSame(inner, QuickNavigationMenu.FindVisualParent<Grid>(leaf));
    }
}
