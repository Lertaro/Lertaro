using Lertaro.App.ViewModels.Settings;

namespace Lertaro.App.Tests.ViewModels.Settings;

[TestClass]
public sealed class FavoriteItemViewModelTests
{
    [TestMethod]
    public void DisplayName_ExplicitName_ReturnsIt() =>
        Assert.AreEqual("Docs", new FavoriteItemViewModel { Name = "Docs", Path = @"C:\Documents" }.DisplayName);

    [TestMethod]
    public void DisplayName_WebUrlNoName_ReturnsTrimmedUrl() =>
        Assert.AreEqual("https://example.com", new FavoriteItemViewModel { Path = "  https://example.com  " }.DisplayName);

    [TestMethod]
    public void DisplayName_PlainPathNoName_ReturnsFileName() =>
        Assert.AreEqual("Documents", new FavoriteItemViewModel { Path = @"C:\Users\me\Documents" }.DisplayName);

    [TestMethod]
    public void DisplayName_PlainPathWithTrailingSlashNoName_ReturnsFileName() =>
        Assert.AreEqual("Documents", new FavoriteItemViewModel { Path = @"C:\Users\me\Documents\" }.DisplayName);

    [TestMethod]
    public void DisplayName_EnvironmentVariableInPathNoName_ExpandsAndReturnsFolderName()
    {
        Environment.SetEnvironmentVariable("TEST_FAV_DIR", @"C:\TestDir\Projects");
        try
        {
            var vm = new FavoriteItemViewModel { Path = @"%TEST_FAV_DIR%" };
            Assert.AreEqual("Projects", vm.DisplayName);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TEST_FAV_DIR", null);
        }
    }

    [TestMethod]
    public void DisplayName_ShellVirtualFolderNoName_ResolvesVirtualFolderDisplayName()
    {
        var vm = new FavoriteItemViewModel { Path = "shell:downloads" };
        var expected = PluginSdk.Helpers.ShellPathHelper.GetVirtualFolderDisplayName("shell:downloads", "shell:downloads");
        Assert.AreEqual(expected, vm.DisplayName);
    }

    [TestMethod]
    public void Name_Set_RaisesPropertyChangedForDisplayNameToo()
    {
        var vm = new FavoriteItemViewModel();
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.Name = "New";

        CollectionAssert.Contains(raised, nameof(FavoriteItemViewModel.DisplayName));
    }

    [TestMethod]
    public void Hotkey_Set_ClearsTheHintFromThePreviousCombination()
    {
        // The hint described the old combination; leaving it up while the user types a new one would
        // read as "this new hotkey is also broken".
        var vm = new FavoriteItemViewModel();
        vm.HotkeyHint = "already used by another application";

        vm.Hotkey = "Ctrl+D";

        Assert.AreEqual(string.Empty, vm.HotkeyHint);
    }

    [TestMethod]
    public void Hotkey_SetToTheSameValue_LeavesTheHintAlone()
    {
        var vm = new FavoriteItemViewModel { Hotkey = "Ctrl+D" };
        vm.HotkeyHint = "already used by another application";

        vm.Hotkey = "Ctrl+D";

        Assert.AreEqual("already used by another application", vm.HotkeyHint);
    }

    [TestMethod]
    public void HotkeyHint_DefaultsToEmpty() =>
        Assert.AreEqual(string.Empty, new FavoriteItemViewModel().HotkeyHint);
}
