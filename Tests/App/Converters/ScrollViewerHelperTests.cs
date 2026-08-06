using System.Windows.Controls;
using System.Windows.Documents;
using Lertaro.App.Converters;

namespace Lertaro.App.Tests.Converters;

// The reveal condition behind the auto-hiding overlay scrollbars. Both halves shipped broken: the
// condition itself (a ScrollViewer inside a RichTextBox reads IsMouseOver false over its own text,
// because a Run is a ContentElement and its state never reaches the visual chain) and the lookup that
// finds the control to ask instead.
[TestClass]
public sealed class ScrollViewerHelperTests
{
    [TestMethod]
    public void PointerOverEither_Reveals()
    {
        Assert.IsTrue(ScrollViewerHelper.ComputePointerNear(true, null), "an ordinary ScrollViewer, no text host");
        Assert.IsTrue(ScrollViewerHelper.ComputePointerNear(false, true), "over a RichTextBox's text: only the host knows");
        Assert.IsTrue(ScrollViewerHelper.ComputePointerNear(true, true));
    }

    [TestMethod]
    public void PointerOverNeither_DoesNot()
    {
        Assert.IsFalse(ScrollViewerHelper.ComputePointerNear(false, null));
        Assert.IsFalse(ScrollViewerHelper.ComputePointerNear(false, false));
    }

    [StaTestMethod]
    public void AStandaloneScrollViewer_HasNoTextHost()
    {
        // The case that matters most: every ordinary ScrollViewer in the app goes through here, and an
        // over-eager lookup would hand it some unrelated control's hover state.
        var scrollViewer = new ScrollViewer { Content = new TextBlock { Text = "x" } };
        Realize(scrollViewer);

        Assert.IsNull(ScrollViewerHelper.FindTextHost(scrollViewer));
    }

    [StaTestMethod]
    public void AContentHostScrollViewer_FindsItsTextBox()
    {
        var textBox = new TextBox { AcceptsReturn = true, Text = "a\nb\nc" };
        Realize(textBox);

        var inner = FindScrollViewer(textBox);
        Assert.IsNotNull(inner, "the TextBox template hosts its text in a ScrollViewer");
        Assert.AreSame(textBox, ScrollViewerHelper.FindTextHost(inner));
    }

    [StaTestMethod]
    public void AContentHostScrollViewer_FindsItsRichTextBox()
    {
        // The control the whole thing is for. RichTextBox derives from TextBoxBase just as TextBox
        // does, so one lookup covers both.
        var richTextBox = new RichTextBox();
        richTextBox.Document.Blocks.Add(new Paragraph(new Run("a\nb\nc")));
        Realize(richTextBox);

        var inner = FindScrollViewer(richTextBox);
        Assert.IsNotNull(inner);
        Assert.AreSame(richTextBox, ScrollViewerHelper.FindTextHost(inner));
    }

    [StaTestMethod]
    public void AScrollViewerAroundATextBox_IsNotThatTextBoxsHost()
    {
        // A page-level ScrollViewer that happens to contain a text box. Walking up from it must not
        // reach anything: its own reveal has to stay driven by its own hover, and a text box further
        // OUT is not what the lookup is after either -- only the one this ScrollViewer belongs to.
        var outer = new ScrollViewer { Content = new TextBox { Text = "x" } };
        Realize(outer);

        Assert.IsNull(ScrollViewerHelper.FindTextHost(outer));
    }

    [StaTestMethod]
    public void ATextBoxInsideAScrollViewer_StillFindsTheTextBoxAndNotTheScrollViewer()
    {
        // The mirror of the above, from the inner ScrollViewer's side: the nearest match wins, so a
        // text box nested in a scrolling page reveals its own scrollbar rather than the page's.
        var textBox = new TextBox { AcceptsReturn = true, Text = "a\nb\nc" };
        var outer = new ScrollViewer { Content = textBox };
        Realize(outer);
        textBox.ApplyTemplate();

        var inner = FindScrollViewer(textBox);
        Assert.IsNotNull(inner);
        Assert.AreSame(textBox, ScrollViewerHelper.FindTextHost(inner));
    }

    // Templates are only applied, and the visual tree only built, once an element is measured -- and a
    // TextBox measured while it is still its own root does not get one at all, so everything is hosted
    // in a Grid first.
    private static void Realize(System.Windows.FrameworkElement element)
    {
        var host = new Grid();
        host.Children.Add(element);
        host.Measure(new System.Windows.Size(200, 100));
        host.Arrange(new System.Windows.Rect(0, 0, 200, 100));
        host.UpdateLayout();
        element.ApplyTemplate();
    }

    private static ScrollViewer? FindScrollViewer(System.Windows.DependencyObject root)
    {
        var count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is ScrollViewer hit) return hit;
            if (FindScrollViewer(child) is { } deeper) return deeper;
        }
        return null;
    }
}
