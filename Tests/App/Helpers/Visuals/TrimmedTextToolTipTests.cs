using System.Windows;
using System.Windows.Controls;
using Lertaro.App.Helpers.Visuals;

namespace Lertaro.App.Tests.Helpers.Visuals;

[TestClass]
public sealed class TrimmedTextToolTipTests
{
    [StaTestMethod]
    public void IsTrimmed_ReturnsFalseWhenTextFits()
        => Assert.IsFalse(TrimmedTextToolTip.IsTrimmed(Arrange("short", 200)));

    [StaTestMethod]
    public void IsTrimmed_ReturnsTrueWhenTextExceedsWidth()
        => Assert.IsTrue(TrimmedTextToolTip.IsTrimmed(Arrange("a much longer item name", 24)));

    [StaTestMethod]
    public void IsTrimmed_ReturnsFalseWhenTrimmingIsDisabled()
    {
        var textBlock = Arrange("a much longer item name", 24);
        textBlock.TextTrimming = TextTrimming.None;

        Assert.IsFalse(TrimmedTextToolTip.IsTrimmed(textBlock));
    }

    [StaTestMethod]
    public void ShouldShowToolTip_ReturnsTrueWhenAnyChildIsTrimmed()
    {
        var panel = new StackPanel();
        panel.Children.Add(Arrange("short", 200));
        panel.Children.Add(Arrange("a much longer size value", 24));

        Assert.IsTrue(TrimmedTextToolTip.ShouldShowToolTip(panel));
    }

    [StaTestMethod]
    public void ShouldShowToolTip_ReturnsFalseWhenAllChildrenFit()
    {
        var panel = new StackPanel();
        panel.Children.Add(Arrange("name", 200));
        panel.Children.Add(Arrange("10 MB", 200));

        Assert.IsFalse(TrimmedTextToolTip.ShouldShowToolTip(panel));
    }

    [StaTestMethod]
    public void IsTrimmed_ReturnsTrueWhenTextIsHidden()
    {
        var textBlock = Arrange("10 MB", 200);
        textBlock.Visibility = Visibility.Collapsed;

        Assert.IsTrue(TrimmedTextToolTip.IsTrimmed(textBlock));
    }

    private static TextBlock Arrange(string text, double width)
    {
        var textBlock = new TextBlock
        {
            Text = text,
            Width = width,
            FontSize = 13,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        textBlock.Measure(new Size(width, 40));
        textBlock.Arrange(new Rect(0, 0, width, Math.Max(20, textBlock.DesiredSize.Height)));
        return textBlock;
    }
}
