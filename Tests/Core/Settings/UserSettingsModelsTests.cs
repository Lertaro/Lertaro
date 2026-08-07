namespace Lertaro.Core.Tests.Settings;

[TestClass]
public sealed class UserSettingsModelsTests
{
    [TestMethod]
    public void HotkeyPageSettings_DisablesDoubleClickQuickNavByDefault() =>
        Assert.IsFalse(new HotkeyPageSettings().QuickNavTriggerOnDoubleClick);
}
