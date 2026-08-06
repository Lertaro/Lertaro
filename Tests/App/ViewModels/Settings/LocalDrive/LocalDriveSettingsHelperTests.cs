using Lertaro.App.ViewModels.Settings.LocalDrive;

namespace Lertaro.App.Tests.ViewModels.Settings.LocalDrive;

[TestClass]
public sealed class LocalDriveSettingsHelperTests
{
    [TestMethod]
    [DataRow("ready", "[Local_StateReady]")]
    [DataRow("indexing", "[Local_StateIndexing]")]
    [DataRow("loading-cache", "[Local_StateLoadingCache]")]
    [DataRow("pending", "[Local_StatePending]")]
    [DataRow("disabled", "[Local_StateDisabled]")]
    [DataRow("unavailable", "[Local_DriveUnavailable]")]
    [DataRow("failed", "[Local_StateFailed]")]
    [DataRow("error", "[Local_StateError]")]
    [DataRow("idle", "[Local_StateIdle]")]
    public void TranslateState_KnownState_MapsToTranslationKey(string state, string expectedKey) =>
        Assert.AreEqual(expectedKey, LocalDriveSettingsHelper.TranslateState(state));

    [TestMethod]
    public void TranslateState_UnknownState_ReturnsRawStateUnchanged() =>
        Assert.AreEqual("some-unknown-state", LocalDriveSettingsHelper.TranslateState("some-unknown-state"));
}

[TestClass]
public sealed class LocalDriveSettingsItemTests
{
    [TestMethod]
    public void RowAction_None_IsNotVisible() =>
        Assert.IsFalse(new LocalDriveSettingsItem { RowAction = LocalDriveRowAction.None }.IsRowActionVisible);

    [TestMethod]
    [DataRow(LocalDriveRowAction.Rebuild, "[Local_RowRebuildBtn]")]
    [DataRow(LocalDriveRowAction.Delete, "[Local_RowDeleteBtn]")]
    public void RowActionText_MapsActionToTranslationKey(LocalDriveRowAction action, string expectedKey)
    {
        var item = new LocalDriveSettingsItem { RowAction = action };

        Assert.IsTrue(item.IsRowActionVisible);
        Assert.AreEqual(expectedKey, item.RowActionText);
    }

    [TestMethod]
    public void CanRunRowAction_Set_RaisesPropertyChanged()
    {
        var item = new LocalDriveSettingsItem();
        var raised = false;
        item.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(item.CanRunRowAction)) raised = true; };

        item.CanRunRowAction = true;

        Assert.IsTrue(raised);
    }

    [TestMethod]
    public void NotifyLanguageChanged_RaisesPropertyChangedForRowActionText()
    {
        var item = new LocalDriveSettingsItem();
        var raised = false;
        item.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(item.RowActionText)) raised = true; };

        item.NotifyLanguageChanged();

        Assert.IsTrue(raised);
    }
}
