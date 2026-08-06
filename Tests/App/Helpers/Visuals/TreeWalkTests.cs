using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using Lertaro.App.Helpers.Visuals;

namespace Lertaro.App.Tests.Helpers.Visuals;

// The crash this exists to stop: clicking the highlighted part of a name in the quick panel took the
// whole app down with "System.Windows.Documents.Run is not a Visual or Visual3D". Highlighting splits a
// TextBlock into Runs, a Run is a ContentElement, and every hand-rolled walk up from a mouse event's
// OriginalSource went straight into VisualTreeHelper.GetParent with it.
[TestClass]
public sealed class TreeWalkTests
{
    [StaTestMethod]
    public void Ancestor_StartingAtAHighlightRun_ReachesTheRowItIsIn()
    {
        var run = new Run("readme");
        var text = new TextBlock();
        text.Inlines.Add(run);

        var row = new ListBoxItem { Content = text };
        var list = new ListBox();
        list.Items.Add(row);
        Realize(list);

        Assert.AreSame(row, TreeWalk.Ancestor<ListBoxItem>(run));
        Assert.AreSame(list, TreeWalk.Ancestor<ListBox>(run));
    }

    [StaTestMethod]
    public void Parent_OfARun_IsTheTextBlockHoldingIt()
    {
        var run = new Run("readme");
        var text = new TextBlock();
        text.Inlines.Add(run);

        Assert.AreSame(text, TreeWalk.Parent(run));
    }

    [StaTestMethod]
    public void Ancestor_FindsNothingWhenThereIsNoSuchAncestor()
    {
        var text = new TextBlock();
        text.Inlines.Add(new Run("loose"));
        Realize(text);

        Assert.IsNull(TreeWalk.Ancestor<ListBoxItem>(text.Inlines.FirstInline));
    }

    [StaTestMethod]
    public void Ancestor_StartingAtTheMatchItself_ReturnsIt()
    {
        var list = new ListBox();

        Assert.AreSame(list, TreeWalk.Ancestor<ListBox>(list));
    }

    [TestMethod]
    public void Ancestor_OfNothing_IsNull() => Assert.IsNull(TreeWalk.Ancestor<ListBox>(null));

    // A visual tree only exists once something has been measured and arranged; an unrealized element's
    // parent is null and the walk would stop before it started.
    private static void Realize(FrameworkElement element)
    {
        element.Measure(new Size(500, 500));
        element.Arrange(new Rect(0, 0, 500, 500));
        element.UpdateLayout();
    }
}
