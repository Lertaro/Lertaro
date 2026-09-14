namespace Lertaro.App.Tests.Views.InlineSearchWindow.Helpers;

using Production = Lertaro.App.Views.InlineSearchWindow.Helpers.InlinePathDisplayFormatter;

[TestClass]
public sealed class InlinePathDisplayFormatterTests
{
    [TestMethod]
    public void Format_KeepsFullPathWhenItFits()
    {
        var result = Production.Format(@"D:\A\B\C", 100, text => text.Length);

        Assert.AreEqual(@"D:\A\B\C", result.Text);
        Assert.IsFalse(result.NeedsTextTrimming);
    }

    [TestMethod]
    public void Format_RemovesMiddleDirectoriesAtFirstOverflow()
    {
        var result = Production.Format(@"D:\A\B\C\D\E", 10, text => text.Length);

        Assert.AreEqual(@"D:\A\...\E", result.Text);
        Assert.IsFalse(result.NeedsTextTrimming);
    }

    [TestMethod]
    public void Format_DropsRootWhenRootAndLastCannotFitTogether()
    {
        var result = Production.Format(@"D:\A\B\Last", 9, text => text.Length);

        Assert.AreEqual(@"...\Last", result.Text);
        Assert.IsFalse(result.NeedsTextTrimming);
    }

    [TestMethod]
    public void Format_UsesTextTrimmingWhenLastSegmentCannotFit()
    {
        var result = Production.Format(@"D:\A\Last", 3, text => text.Length);

        Assert.AreEqual(@"D:\A\Last", result.Text);
        Assert.IsTrue(result.NeedsTextTrimming);
    }
}
