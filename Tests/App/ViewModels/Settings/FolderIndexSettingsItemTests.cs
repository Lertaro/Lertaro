using Lertaro.App.ViewModels.Settings;
using Lertaro.App.ViewModels.Settings.NetworkDrive;

namespace Lertaro.App.Tests.ViewModels.Settings;

[TestClass]
public sealed class FolderIndexSettingsItemTests
{
    [TestMethod]
    public void DisplayName_OrdinarySubfolder_ReturnsLastSegment() =>
        Assert.AreEqual("Sub", new FolderIndexSettingsItem { Path = @"\\server\share\Sub" }.DisplayName);

    [TestMethod]
    public void DisplayName_LocalPath_ReturnsLastSegment() =>
        Assert.AreEqual("Documents", new FolderIndexSettingsItem { Path = @"C:\Users\me\Documents" }.DisplayName);

    [TestMethod]
    public void DisplayName_UncShareRoot_FallsBackToShareName() =>
        // GetFileName returns "" for a bare UNC share root since GetPathRoot treats the share as its own
        // root -- this manually extracts the last path segment ("share") in that case.
        Assert.AreEqual("share", new FolderIndexSettingsItem { Path = @"\\server\share" }.DisplayName);

    [TestMethod]
    public void DisplayName_TrailingSlash_IsTrimmedBeforeExtraction() =>
        Assert.AreEqual("Sub", new FolderIndexSettingsItem { Path = @"\\server\share\Sub\" }.DisplayName);

    [TestMethod]
    public void RowActionText_Delete_ReturnsDeleteTranslationKey() =>
        Assert.AreEqual("[Network_RowDeleteBtn]", new FolderIndexSettingsItem { RowAction = NetworkDriveRowAction.Delete }.RowActionText);

    [TestMethod]
    public void RefreshMode_SetToEmptyString_IsRejected()
    {
        var item = new FolderIndexSettingsItem { RefreshMode = "Daily" };

        item.RefreshMode = "";

        Assert.AreEqual("Daily", item.RefreshMode);
    }
}
