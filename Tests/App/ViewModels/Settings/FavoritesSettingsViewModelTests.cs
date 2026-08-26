using Lertaro.Core;
using Lertaro.App.ViewModels.Settings;

namespace Lertaro.App.Tests.ViewModels.Settings;

[TestClass]
public sealed class FavoritesSettingsViewModelTests
{
    [TestMethod]
    public void Constructor_LoadsExistingFavorites()
    {
        var settings = new UserSettings { Favorites = new List<FavoriteItemSetting> { new() { Name = "Docs", Path = @"C:\Docs" } } };

        var vm = new FavoritesSettingsViewModel(settings);

        Assert.HasCount(1, vm.Items);
        Assert.AreEqual("Docs", vm.Items[0].Name);
    }

    [TestMethod]
    public void AddCommand_CanExecute_FalseWhenNewPathBlank()
    {
        var vm = new FavoritesSettingsViewModel(new UserSettings());

        Assert.IsFalse(vm.AddCommand.CanExecute(null));

        vm.NewPath = @"C:\Docs";

        Assert.IsTrue(vm.AddCommand.CanExecute(null));
    }

    [TestMethod]
    public void AddCommand_Execute_AddsTrimmedUnquotedPathAndClearsInputs()
    {
        var vm = new FavoritesSettingsViewModel(new UserSettings()) { NewName = "Docs", NewPath = "  \"C:\\Docs\"  " };

        vm.AddCommand.Execute(null);

        Assert.AreEqual(@"C:\Docs", vm.Items[0].Path);
        Assert.AreEqual("Docs", vm.Items[0].Name);
        Assert.AreEqual("", vm.NewPath);
        Assert.AreEqual("", vm.NewName);
    }

    [TestMethod]
    public void NewPath_BlankName_AutoFillsNameFromFileName()
    {
        var vm = new FavoritesSettingsViewModel(new UserSettings()) { NewPath = @"C:\Users\me\Documents\" };

        Assert.AreEqual("Documents", vm.NewName);
    }

    [TestMethod]
    public void NewPath_WebUrl_DoesNotAutoFillName()
    {
        var vm = new FavoritesSettingsViewModel(new UserSettings()) { NewPath = "https://example.com/docs" };

        Assert.AreEqual("", vm.NewName);
    }

    [TestMethod]
    public void NewPath_ShellVirtualFolder_AutoFillsVirtualName()
    {
        var vm = new FavoritesSettingsViewModel(new UserSettings()) { NewPath = "shell:downloads" };
        var expected = Lertaro.PluginSdk.Helpers.ShellPathHelper.GetVirtualFolderDisplayName("shell:downloads", "");
        Assert.AreEqual(expected, vm.NewName);
    }

    [TestMethod]
    public void NewPath_ExplicitNameAlreadySet_IsNotOverwritten()
    {
        var vm = new FavoritesSettingsViewModel(new UserSettings()) { NewName = "Custom" };

        vm.NewPath = @"C:\Users\me\Documents";

        Assert.AreEqual("Custom", vm.NewName);
    }

    [TestMethod]
    public void RemoveCommand_Execute_RemovesItem()
    {
        var vm = new FavoritesSettingsViewModel(new UserSettings()) { NewPath = @"C:\Docs" };
        vm.AddCommand.Execute(null);
        var item = vm.Items[0];

        vm.RemoveCommand.Execute(item);

        Assert.IsEmpty(vm.Items);
    }

    [TestMethod]
    public void EditCommand_Execute_MovesValuesBackIntoInputsAndRemovesFromList()
    {
        var vm = new FavoritesSettingsViewModel(new UserSettings()) { NewName = "Docs", NewPath = @"C:\Docs" };
        vm.AddCommand.Execute(null);
        var item = vm.Items[0];

        vm.EditCommand.Execute(item);

        Assert.AreEqual("Docs", vm.NewName);
        Assert.AreEqual(@"C:\Docs", vm.NewPath);
        Assert.IsEmpty(vm.Items);
    }

    [TestMethod]
    public void EditCommand_Execute_BlankExplicitName_FallsBackToDisplayName()
    {
        var vm = new FavoritesSettingsViewModel(new UserSettings()) { NewPath = @"C:\Users\me\Documents" };
        vm.AddCommand.Execute(null);
        var item = vm.Items[0];

        vm.EditCommand.Execute(item);

        Assert.AreEqual("Documents", vm.NewName);
    }

    [TestMethod]
    public void MoveUpCommand_Execute_SwapsWithPreviousItem()
    {
        var vm = new FavoritesSettingsViewModel(new UserSettings());
        vm.NewPath = @"C:\a"; vm.AddCommand.Execute(null);
        vm.NewPath = @"C:\b"; vm.AddCommand.Execute(null);
        var second = vm.Items[1];

        vm.MoveUpCommand.Execute(second);

        Assert.AreEqual(@"C:\b", vm.Items[0].Path);
    }

    [TestMethod]
    public void MoveUpCommand_Execute_AlreadyFirst_DoesNothing()
    {
        var vm = new FavoritesSettingsViewModel(new UserSettings()) { NewPath = @"C:\a" };
        vm.AddCommand.Execute(null);
        var first = vm.Items[0];

        vm.MoveUpCommand.Execute(first);

        Assert.AreEqual(@"C:\a", vm.Items[0].Path);
    }

    [TestMethod]
    public void MoveDownCommand_Execute_SwapsWithNextItem()
    {
        var vm = new FavoritesSettingsViewModel(new UserSettings());
        vm.NewPath = @"C:\a"; vm.AddCommand.Execute(null);
        vm.NewPath = @"C:\b"; vm.AddCommand.Execute(null);
        var first = vm.Items[0];

        vm.MoveDownCommand.Execute(first);

        Assert.AreEqual(@"C:\a", vm.Items[1].Path);
    }

    [TestMethod]
    public void MoveDownCommand_Execute_AlreadyLast_DoesNothing()
    {
        var vm = new FavoritesSettingsViewModel(new UserSettings()) { NewPath = @"C:\a" };
        vm.AddCommand.Execute(null);
        var last = vm.Items[0];

        vm.MoveDownCommand.Execute(last);

        Assert.AreEqual(@"C:\a", vm.Items[0].Path);
    }
}

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
        var expected = Lertaro.PluginSdk.Helpers.ShellPathHelper.GetVirtualFolderDisplayName("shell:downloads", "shell:downloads");
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
}
