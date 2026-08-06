using System.Windows;
using System.Windows.Controls;
using Lertaro.App.Helpers.Visuals;
using Lertaro.App.Services.ShellMenu.QuickNav;

namespace Lertaro.App.Tests.Services.ShellMenu.QuickNav;

// Regression coverage for GitHub issue #184: a long file/folder name used to make the whole cascading
// ContextMenu/Popup auto-size to fit it, dragging every other row's column out just as wide.
// CreateItemHeader is what caps a single row's own text width and wires in the same MarqueeBehavior
// DataTemplates.xaml's search-result rows already use, so hovering an overflowing row still reveals its
// full name instead of leaving it permanently cut off.
[TestClass]
public sealed class QuickNavigationMenuContentExtensionsTests
{
    [StaTestMethod]
    public void CreateItemHeader_CapsWidthToMaxItemTextWidth()
    {
        var header = QuickNavigationMenuContentExtensions.CreateItemHeader("a-reasonably-long-folder-name");

        Assert.AreEqual(220, header.MaxWidth);
    }

    [StaTestMethod]
    public void CreateItemHeader_HidesHorizontalScrollbarButStillClips()
    {
        var header = QuickNavigationMenuContentExtensions.CreateItemHeader("some text");

        Assert.AreEqual(ScrollBarVisibility.Hidden, header.HorizontalScrollBarVisibility);
        Assert.AreEqual(ScrollBarVisibility.Disabled, header.VerticalScrollBarVisibility);
    }

    [StaTestMethod]
    public void CreateItemHeader_NotFocusable_SoTabOrderSkipsIt()
    {
        var header = QuickNavigationMenuContentExtensions.CreateItemHeader("some text");

        Assert.IsFalse(header.Focusable);
    }

    [StaTestMethod]
    public void CreateItemHeader_ContentIsTextBlockWithTheGivenText()
    {
        var header = QuickNavigationMenuContentExtensions.CreateItemHeader("Projects (very long client name here)");

        var textBlock = header.Content as TextBlock;
        Assert.IsNotNull(textBlock);
        Assert.AreEqual("Projects (very long client name here)", textBlock.Text);
    }

    [StaTestMethod]
    public void CreateItemHeader_TextTrimmingIsNone_SoMarqueeCanRevealTheFullName()
    {
        var header = QuickNavigationMenuContentExtensions.CreateItemHeader("some text");

        var textBlock = (TextBlock)header.Content;
        Assert.AreEqual(TextTrimming.None, textBlock.TextTrimming);
    }

    [StaTestMethod]
    public void CreateItemHeader_EnablesMarqueeOnTheTextBlock()
    {
        var header = QuickNavigationMenuContentExtensions.CreateItemHeader("some text");

        var textBlock = (TextBlock)header.Content;
        Assert.IsTrue(MarqueeBehavior.GetEnableMarquee(textBlock));
    }
}
