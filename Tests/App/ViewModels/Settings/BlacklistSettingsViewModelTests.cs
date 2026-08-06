using Lertaro.Core;
using Lertaro.App.ViewModels.Settings;

namespace Lertaro.App.Tests.ViewModels.Settings;

[TestClass]
public sealed class BlacklistSettingsViewModelTests
{
    [TestMethod]
    public void Constructor_LoadsExistingBlacklistedProcesses()
    {
        var settings = new UserSettings { BlacklistedProcesses = new List<string> { "explorer.exe", "notepad.exe" } };

        var vm = new BlacklistSettingsViewModel(settings);

        Assert.HasCount(2, vm.Global.Items);
        Assert.AreEqual("explorer.exe" + Environment.NewLine + "notepad.exe", vm.Global.BulkText);
    }

    [TestMethod]
    public void Constructor_SkipsBlankEntries()
    {
        var settings = new UserSettings { BlacklistedProcesses = new List<string> { "a.exe", "  ", "" } };

        var vm = new BlacklistSettingsViewModel(settings);

        Assert.HasCount(1, vm.Global.Items);
    }

    [TestMethod]
    public void AddProcessCommand_CanExecute_FalseWhenNewProcessNameBlank()
    {
        var vm = new BlacklistSettingsViewModel(new UserSettings());

        Assert.IsFalse(vm.Global.AddProcessCommand.CanExecute(null));

        vm.Global.NewProcessName = "a.exe";

        Assert.IsTrue(vm.Global.AddProcessCommand.CanExecute(null));
    }

    [TestMethod]
    public void AddProcessCommand_Execute_AddsTrimmedUnquotedNameAndClearsInput()
    {
        var vm = Build(newName: "  \"chrome.exe\"  ");

        vm.Global.AddProcessCommand.Execute(null);

        Assert.AreEqual("chrome.exe", vm.Global.Items[0].Value);
        Assert.AreEqual("", vm.Global.NewProcessName);
    }

    [TestMethod]
    public void AddProcessCommand_Execute_DuplicateNameCaseInsensitive_IsNotAddedTwice()
    {
        var vm = Build(newName: "chrome.exe");
        vm.Global.AddProcessCommand.Execute(null);
        vm.Global.NewProcessName = "CHROME.EXE";

        vm.Global.AddProcessCommand.Execute(null);

        Assert.HasCount(1, vm.Global.Items);
    }

    [TestMethod]
    public void RemoveProcessCommand_Execute_RemovesItemAndRefreshesText()
    {
        var vm = Build(newName: "a.exe");
        vm.Global.AddProcessCommand.Execute(null);
        var item = vm.Global.Items[0];

        vm.Global.RemoveProcessCommand.Execute(item);

        Assert.IsEmpty(vm.Global.Items);
        Assert.AreEqual("", vm.Global.BulkText);
    }

    [TestMethod]
    public void EditProcessCommand_Execute_MovesValueBackIntoInputAndRemovesFromList()
    {
        var vm = Build(newName: "a.exe");
        vm.Global.AddProcessCommand.Execute(null);
        var item = vm.Global.Items[0];

        vm.Global.EditProcessCommand.Execute(item);

        Assert.AreEqual("a.exe", vm.Global.NewProcessName);
        Assert.IsEmpty(vm.Global.Items);
    }

    [TestMethod]
    public void ApplyTextCommand_Execute_ParsesMultilineTextIntoDistinctTrimmedItems()
    {
        var vm = Build(bulkText: "a.exe\r\n\"b.exe\"\nA.EXE\n  \n");

        vm.Global.ApplyTextCommand.Execute(null);

        CollectionAssert.AreEqual(new[] { "a.exe", "b.exe" }, vm.Global.Items.Select(x => x.Value).ToList());
    }

    [TestMethod]
    public void ExportTextCommand_Execute_RewritesBlacklistTextFromCurrentItems()
    {
        var vm = Build(newName: "a.exe");
        vm.Global.AddProcessCommand.Execute(null);
        vm.Global.BulkText = "stale text";

        vm.Global.ExportTextCommand.Execute(null);

        Assert.AreEqual("a.exe", vm.Global.BulkText);
    }

    [TestMethod]
    public void Save_WritesNormalizedListBackToUserSettings()
    {
        var settings = new UserSettings();
        var vm = Build(bulkText: "a.exe\nA.EXE\nb.exe", settings: settings);

        vm.Save();

        CollectionAssert.AreEqual(new[] { "a.exe", "b.exe" }, settings.BlacklistedProcesses);
    }

    // The editor moved out of this type (ProcessBlacklistEditorViewModel, shared with the quick
    // panel's own lists), so these reach it through Global rather than setting fields here.
    private static BlacklistSettingsViewModel Build(string? newName = null, string? bulkText = null, UserSettings? settings = null)
    {
        var vm = new BlacklistSettingsViewModel(settings ?? new UserSettings());
        if (newName != null) vm.Global.NewProcessName = newName;
        if (bulkText != null) vm.Global.BulkText = bulkText;
        return vm;
    }
}
