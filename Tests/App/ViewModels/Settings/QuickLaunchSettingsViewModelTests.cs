using System.IO;
using Lertaro.Core;
using Lertaro.App.ViewModels.Settings;

namespace Lertaro.App.Tests.ViewModels.Settings;

[TestClass]
public sealed class QuickLaunchSettingsViewModelTests
{
    [TestMethod]
    public void EditCommand_Execute_StartsInlineEditWithoutRemovingItem()
    {
        var vm = new QuickLaunchSettingsViewModel(new UserSettings());
        var path = Path.GetTempPath();
        vm.NewPath = path;
        vm.AddCommand.Execute(null);
        var item = vm.Items[0];

        vm.EditCommand.Execute(item);

        Assert.HasCount(1, vm.Items);
        Assert.IsTrue(item.IsEditing);
        Assert.AreEqual(item.Name, item.EditName);
        Assert.AreEqual(path, item.EditPath);
    }

    [TestMethod]
    public void AddPaths_AddsAllUniqueExistingPathsWithAutomaticNames()
    {
        var first = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".exe");
        var second = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".cmd");
        File.WriteAllText(first, string.Empty);
        File.WriteAllText(second, string.Empty);
        try
        {
            var vm = new QuickLaunchSettingsViewModel(new UserSettings());

            vm.AddPaths(new[] { first, second, first });

            Assert.HasCount(2, vm.Items);
            Assert.AreEqual(Path.GetFileNameWithoutExtension(first), vm.Items[0].Name);
            Assert.AreEqual(Path.GetFileNameWithoutExtension(second), vm.Items[1].Name);
        }
        finally
        {
            File.Delete(first);
            File.Delete(second);
        }
    }
}
