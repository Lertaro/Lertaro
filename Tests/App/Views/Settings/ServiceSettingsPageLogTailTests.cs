using Lertaro.App.Views.Settings;

namespace Lertaro.App.Tests.Views.Settings;

// The width heuristic used to select candidate log lines for measurement.
[TestClass]
public sealed class ServiceSettingsPageLogTailTests
{
    [TestMethod]
    public void WeightedLength_CountsFullWidthCharactersTwice()
    {
        // The log is monospaced: a CJK/full-width glyph takes twice the advance of a Latin one, so this
        // weighting is what lets the widest line be picked without measuring every line.
        Assert.AreEqual(3, ServiceSettingsPage.WeightedLength("abc"));
        Assert.AreEqual(8, ServiceSettingsPage.WeightedLength("中文测试"));
        Assert.AreEqual(6, ServiceSettingsPage.WeightedLength("ab中文"));
    }

    [TestMethod]
    public void WeightedLength_RanksByRenderedWidthNotCharacterCount()
    {
        // Six CJK characters (weight 12) render wider than ten Latin ones (weight 10), even though the
        // Latin string has more characters. String.Length would rank them the other way round.
        var latin = new string('a', 10);
        var cjk = new string('中', 6);

        Assert.IsGreaterThan(ServiceSettingsPage.WeightedLength(latin), ServiceSettingsPage.WeightedLength(cjk));
    }
}
