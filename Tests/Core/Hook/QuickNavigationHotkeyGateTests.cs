using Lertaro.Core.Hook.InlineSearch;

namespace Lertaro.Core.Tests.Hook;

[TestClass]
public sealed class QuickNavigationHotkeyGateTests
{
    [TestMethod]
    public void ShouldSuppress_FileOperationWindow_AlwaysAllows() => Assert.IsFalse(QuickNavigationHotkeyGate.ShouldSuppress(true, true, true, true));

    [TestMethod]
    public void ShouldSuppress_OrdinaryWindow_HonorsEachProtection()
    {
        Assert.IsTrue(QuickNavigationHotkeyGate.ShouldSuppress(false, false, true, false));
        Assert.IsTrue(QuickNavigationHotkeyGate.ShouldSuppress(false, false, false, true));
        Assert.IsTrue(QuickNavigationHotkeyGate.ShouldSuppress(false, true, false, false));
        Assert.IsFalse(QuickNavigationHotkeyGate.ShouldSuppress(false, false, false, false));
    }
}
