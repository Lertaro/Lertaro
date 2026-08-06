using Lertaro.App.ViewModels.Settings.NetworkDrive;

namespace Lertaro.App.Tests.ViewModels.Settings.NetworkDrive;

[TestClass]
public sealed class NetworkDriveSettingsItemTests
{
    [TestMethod]
    public void RefreshMode_SetToEmptyString_IsRejectedAndKeepsPreviousValue()
    {
        var item = new NetworkDriveSettingsItem { RefreshMode = "Hourly" };

        item.RefreshMode = "";

        Assert.AreEqual("Hourly", item.RefreshMode);
    }

    [TestMethod]
    public void RefreshMode_SetToEmptyString_StillRaisesPropertyChangedToResyncBinding()
    {
        var item = new NetworkDriveSettingsItem();
        var raised = false;
        item.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(item.RefreshMode)) raised = true; };

        item.RefreshMode = "";

        Assert.IsTrue(raised);
    }

    [TestMethod]
    [DataRow("Manual", "[Network_ModeManual]")]
    [DataRow("15Minutes", "[Network_Mode15M]")]
    [DataRow("Hourly", "[Network_ModeHourly]")]
    [DataRow("Daily", "[Network_ModeDaily]")]
    public void RefreshModeText_MapsRefreshModeToTranslationKey(string mode, string expectedKey)
    {
        var item = new NetworkDriveSettingsItem { RefreshMode = mode };

        Assert.AreEqual(expectedKey, item.RefreshModeText);
    }

    [TestMethod]
    public void RefreshModeText_UnknownMode_FallsBackToRawValue()
    {
        // Unreachable via the RefreshMode setter's own validation in practice, but RefreshModeText's
        // switch has a raw-passthrough default branch worth pinning regardless.
        var item = new NetworkDriveSettingsItem();
        typeof(NetworkDriveSettingsItem).GetField("_refreshMode", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(item, "Weekly");

        Assert.AreEqual("Weekly", item.RefreshModeText);
    }

    [TestMethod]
    public void IsRowActionVisible_ActionIsNone_ReturnsFalse()
    {
        var item = new NetworkDriveSettingsItem { RowAction = NetworkDriveRowAction.None };

        Assert.IsFalse(item.IsRowActionVisible);
    }

    [TestMethod]
    [DataRow(NetworkDriveRowAction.Rebuild, "[Network_RowRebuildBtn]")]
    [DataRow(NetworkDriveRowAction.Delete, "[Network_RowDeleteBtn]")]
    [DataRow(NetworkDriveRowAction.Stop, "[Network_RowStopBtn]")]
    public void RowActionText_MapsActionToTranslationKey(NetworkDriveRowAction action, string expectedKey)
    {
        var item = new NetworkDriveSettingsItem { RowAction = action };

        Assert.IsTrue(item.IsRowActionVisible);
        Assert.AreEqual(expectedKey, item.RowActionText);
    }

    [TestMethod]
    public void CanRunRowAction_Set_RaisesPropertyChanged()
    {
        var item = new NetworkDriveSettingsItem();
        var raised = false;
        item.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(item.CanRunRowAction)) raised = true; };

        item.CanRunRowAction = true;

        Assert.IsTrue(raised);
    }

    [TestMethod]
    public void NotifyLanguageChanged_RaisesPropertyChangedForRefreshModeTextAndRowActionText()
    {
        var item = new NetworkDriveSettingsItem();
        var raised = new List<string?>();
        item.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        item.NotifyLanguageChanged();

        CollectionAssert.Contains(raised, nameof(item.RefreshModeText));
        CollectionAssert.Contains(raised, nameof(item.RowActionText));
    }
}
