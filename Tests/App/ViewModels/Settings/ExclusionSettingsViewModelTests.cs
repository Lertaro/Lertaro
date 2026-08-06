using Lertaro.Core;
using Lertaro.App.ViewModels.Settings;

namespace Lertaro.App.Tests.ViewModels.Settings;

[TestClass]
public sealed class ExclusionSettingsViewModelTests
{
    // UserSettings.ExcludedPaths/IgnoredPathGlobs ship with real, non-empty defaults (see UserSettings.cs)
    // -- unlike BlacklistedProcesses/Favorites/IgnoredPathRegexes, which default empty -- so tests that
    // need a clean starting collection must override all three explicitly rather than relying on `new
    // UserSettings()` alone.
    private static UserSettings EmptySettings() => new()
    {
        ExcludedPaths = new List<string>(),
        IgnoredPathGlobs = new List<string>(),
        IgnoredPathRegexes = new List<string>(),
    };

    [TestMethod]
    public void Constructor_LoadsExistingPathsGlobsAndRegexes()
    {
        var settings = new UserSettings
        {
            ExcludedPaths = new List<string> { @"C:\temp" },
            IgnoredPathGlobs = new List<string> { "*.tmp" },
            IgnoredPathRegexes = new List<string> { "^cache" },
        };

        var vm = new ExclusionSettingsViewModel(settings);

        Assert.HasCount(1, vm.ExcludedPaths);
        Assert.HasCount(1, vm.IgnoredGlobs);
        Assert.HasCount(1, vm.IgnoredRegexes);
    }

    [TestMethod]
    public void AddPathCommand_CanExecute_TracksNewExcludedPathBlankState()
    {
        var vm = new ExclusionSettingsViewModel(EmptySettings());

        Assert.IsFalse(vm.AddPathCommand.CanExecute(null));

        vm.NewExcludedPath = @"C:\temp";

        Assert.IsTrue(vm.AddPathCommand.CanExecute(null));
    }

    [TestMethod]
    public void AddPathCommand_Execute_AddsTrimmedUnquotedPathAndClearsInput()
    {
        var vm = new ExclusionSettingsViewModel(EmptySettings()) { NewExcludedPath = "  \"C:\\temp\"  " };

        vm.AddPathCommand.Execute(null);

        Assert.AreEqual(@"C:\temp", vm.ExcludedPaths[0].Value);
        Assert.AreEqual("", vm.NewExcludedPath);
    }

    [TestMethod]
    public void AddGlobCommand_Execute_AddsToIgnoredGlobsOnly()
    {
        var vm = new ExclusionSettingsViewModel(EmptySettings()) { NewIgnoredGlob = "*.tmp" };

        vm.AddGlobCommand.Execute(null);

        Assert.HasCount(1, vm.IgnoredGlobs);
        Assert.IsEmpty(vm.ExcludedPaths);
    }

    [TestMethod]
    public void AddRegexCommand_Execute_DuplicateCaseInsensitive_IsNotAddedTwice()
    {
        var vm = new ExclusionSettingsViewModel(EmptySettings()) { NewIgnoredRegex = "^cache" };
        vm.AddRegexCommand.Execute(null);
        vm.NewIgnoredRegex = "^CACHE";

        vm.AddRegexCommand.Execute(null);

        Assert.HasCount(1, vm.IgnoredRegexes);
    }

    [TestMethod]
    public void RemovePathCommand_Execute_RemovesFromExcludedPathsOnly()
    {
        var vm = new ExclusionSettingsViewModel(EmptySettings()) { NewExcludedPath = @"C:\temp" };
        vm.AddPathCommand.Execute(null);
        var item = vm.ExcludedPaths[0];

        vm.RemovePathCommand.Execute(item);

        Assert.IsEmpty(vm.ExcludedPaths);
    }

    [TestMethod]
    public void EditGlobCommand_Execute_MovesValueBackIntoInputAndRemovesFromList()
    {
        var vm = new ExclusionSettingsViewModel(EmptySettings()) { NewIgnoredGlob = "*.tmp" };
        vm.AddGlobCommand.Execute(null);
        var item = vm.IgnoredGlobs[0];

        vm.EditGlobCommand.Execute(item);

        Assert.AreEqual("*.tmp", vm.NewIgnoredGlob);
        Assert.IsEmpty(vm.IgnoredGlobs);
    }

    [TestMethod]
    public void ApplyPathsTextCommand_Execute_ParsesMultilineTextIntoDistinctPaths()
    {
        var vm = new ExclusionSettingsViewModel(EmptySettings())
        {
            ExcludedPathsText = "C:\\a\r\nC:\\B\nc:\\a\n  \n"
        };

        vm.ApplyPathsTextCommand.Execute(null);

        Assert.HasCount(2, vm.ExcludedPaths);
    }

    [TestMethod]
    public void ExportGlobsTextCommand_Execute_RewritesTextFromCurrentGlobItems()
    {
        var vm = new ExclusionSettingsViewModel(EmptySettings()) { NewIgnoredGlob = "*.tmp" };
        vm.AddGlobCommand.Execute(null);
        vm.IgnoredGlobsText = "stale";

        vm.ExportGlobsTextCommand.Execute(null);

        Assert.AreEqual("*.tmp", vm.IgnoredGlobsText);
    }

    [TestMethod]
    public void SelectSubTabCommand_Execute_UpdatesSelectedSubTab()
    {
        var vm = new ExclusionSettingsViewModel(EmptySettings());

        vm.SelectSubTabCommand.Execute("Glob");

        Assert.AreEqual("Glob", vm.SelectedSubTab);
    }

    [TestMethod]
    public void Save_WritesNormalizedListsBackToUserSettingsIndependently()
    {
        // Populated via the Add commands (which keep the ObservableCollections and their bulk-text
        // mirrors consistent with each other), not by setting the *Text properties directly and calling
        // Save(): ApplyBulkText's own RefreshBulkText() call resyncs ALL THREE *Text properties from
        // their (still perhaps out-of-sync) collections after each one is applied in turn, so setting
        // all three text properties up front and calling Save() once would have the first ApplyBulkText
        // call silently clobber the other two back to whatever their collections already held.
        var settings = EmptySettings();
        var vm = new ExclusionSettingsViewModel(settings);
        vm.NewExcludedPath = @"C:\a"; vm.AddPathCommand.Execute(null);
        vm.NewIgnoredGlob = "*.tmp"; vm.AddGlobCommand.Execute(null);
        vm.NewIgnoredRegex = "^cache"; vm.AddRegexCommand.Execute(null);

        vm.Save();

        CollectionAssert.AreEqual(new[] { @"C:\a" }, settings.ExcludedPaths);
        CollectionAssert.AreEqual(new[] { "*.tmp" }, settings.IgnoredPathGlobs);
        CollectionAssert.AreEqual(new[] { "^cache" }, settings.IgnoredPathRegexes);
    }
}
