using Lertaro.App.Views.Settings;

namespace Lertaro.App.Tests.Views.Settings;

[TestClass]
public sealed class SettingsSidebarLayoutTests
{
    [TestMethod]
    public void IsCompact_AtOrBelowThreshold_ReturnsTrue()
    {
        Assert.IsTrue(SettingsSidebarLayout.IsCompact(SettingsSidebarLayout.CompactThreshold));
        Assert.IsTrue(SettingsSidebarLayout.IsCompact(SettingsSidebarLayout.CompactThreshold - 1));
    }

    [TestMethod]
    public void IsCompact_AboveThreshold_ReturnsFalse() => Assert.IsFalse(
        SettingsSidebarLayout.IsCompact(SettingsSidebarLayout.CompactThreshold + 1));
}
