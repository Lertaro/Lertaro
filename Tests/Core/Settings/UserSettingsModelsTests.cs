namespace Lertaro.Core.Tests.Settings;

[TestClass]
public sealed class UserSettingsModelsTests
{
    [TestMethod]
    public void LocalSendSettingsModel_EnablesHttpsByDefault() =>
        Assert.IsTrue(new LocalSendSettingsModel().EnableHttps);

    [TestMethod]
    public void HotkeyPageSettings_DisablesDoubleClickQuickNavByDefault() =>
        Assert.IsFalse(new HotkeyPageSettings().QuickNavTriggerOnDoubleClick);

    [TestMethod]
    public void MainWindowSettings_AllowsMultipleInstancesByDefault() =>
        Assert.IsFalse(new MainWindowSettings().SingleInstance);

    [TestMethod]
    public void DefaultFileManagerSetting_DisablesExplorerTabsByDefault() =>
        Assert.IsFalse(new DefaultFileManagerSetting().OpenFoldersInNewExplorerTabs);
}
