using Lertaro.App.ViewModels.Settings;
using Lertaro.App.ViewModels.Settings.NetworkDrive;

namespace Lertaro.App.Tests.ViewModels.Settings;

[TestClass]
public sealed class WslSettingsItemTests
{
    [TestMethod]
    public void UncPath_DerivedFromDistroName() =>
        Assert.AreEqual(@"\\wsl$\Ubuntu", new WslSettingsItem { DistroName = "Ubuntu" }.UncPath);

    [TestMethod]
    [DataRow(NetworkDriveRowAction.Rebuild, "[Network_RowRebuildBtn]")]
    [DataRow(NetworkDriveRowAction.Stop, "[Network_RowStopBtn]")]
    public void RowActionText_MapsActionToTranslationKey(NetworkDriveRowAction action, string expectedKey) =>
        Assert.AreEqual(expectedKey, new WslSettingsItem { RowAction = action }.RowActionText);

    [TestMethod]
    public void RowAction_None_IsNotVisible() =>
        Assert.IsFalse(new WslSettingsItem { RowAction = NetworkDriveRowAction.None }.IsRowActionVisible);

    [TestMethod]
    public void RefreshMode_SetToEmptyString_IsRejected()
    {
        var item = new WslSettingsItem { RefreshMode = "15Minutes" };

        item.RefreshMode = "";

        Assert.AreEqual("15Minutes", item.RefreshMode);
    }

    [TestMethod]
    public void NotifyLanguageChanged_RaisesPropertyChangedForDisplayText()
    {
        var item = new WslSettingsItem();
        var raised = new List<string?>();
        item.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        item.NotifyLanguageChanged();

        CollectionAssert.Contains(raised, nameof(item.RefreshModeText));
        CollectionAssert.Contains(raised, nameof(item.RowActionText));
    }
}
