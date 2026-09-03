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
    public void ClearCommand_Execute_ClearsItemsAndUpdatesHasItems()
    {
        var vm = new QuickLaunchSettingsViewModel(new UserSettings()) { NewPath = Path.GetTempPath() };
        vm.AddCommand.Execute(null);

        Assert.IsTrue(vm.HasItems);

        vm.ClearCommand.Execute(null);

        Assert.IsEmpty(vm.Items);
        Assert.IsFalse(vm.HasItems);
    }

    [TestMethod]
    public void AddPaths_AddsAllUniqueExistingPathsWithAutomaticNames()
    {
        // Real system executables instead of fixture files: creating an *.exe/*.cmd in the temp
        // directory trips antivirus real-time protection on some machines (UnauthorizedAccessException
        // on CreateFile), and this test only needs paths that EXIST. The automatic-name behavior under
        // test is extension-driven (known executable extensions get hidden), which System32 binaries
        // exercise identically.
        var first = Path.Combine(Environment.SystemDirectory, "notepad.exe");
        var second = Path.Combine(Environment.SystemDirectory, "cmd.exe");

        var vm = new QuickLaunchSettingsViewModel(new UserSettings());

        vm.AddPaths(new[] { first, second, first });

        Assert.HasCount(2, vm.Items);
        Assert.AreEqual(Path.GetFileNameWithoutExtension(first), vm.Items[0].Name);
        Assert.AreEqual(Path.GetFileNameWithoutExtension(second), vm.Items[1].Name);
    }
}
