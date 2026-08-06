using Lertaro.App.ViewModels.Settings.QuickPanel;
using Lertaro.Core;

namespace Lertaro.App.Tests.ViewModels.Settings.QuickPanel;

// What a folder looks like the moment it is added. Split from QuickPanelSettingsViewModelTests only to
// keep that file under the repo's per-file line limit.
[TestClass]
public sealed class QuickPanelAddSourceTests
{
    private static UserSettings BuildSettings(string existingFolder)
    {
        var tab = new QuickPanelTab { Id = "tab1" };
        tab.Folders.Add(QuickPanelFolderSource.For(existingFolder));

        var settings = new UserSettings();
        settings.QuickPanel.Tabs = new List<QuickPanelTab> { tab };
        settings.QuickPanel.ActiveTabId = tab.Id;
        return settings;
    }

    // Folders hide the files this panel is normally opened to reach, so a picked source starts with files
    // only. The other display kinds remain available from the row's dropdown.
    [TestMethod]
    public void AddedFolder_StartsAsFilesOnly()
    {
        var settings = BuildSettings(@"C:\a");
        var vm = new QuickPanelSettingsViewModel(settings);
        var tab = vm.Tabs.Single();

        tab.AddFolders(new[] { @"C:\projects" });

        Assert.AreEqual(QuickPanelSourceKind.FilesOnly, tab.Sources.Single(s => s.Path == @"C:\projects").Kind);

        vm.Save();
        Assert.AreEqual(
            QuickPanelSourceKind.FilesOnly,
            settings.QuickPanel.Tabs.Single().Folders.Single(f => f.Path == @"C:\projects").Kind);
    }

    // The fresh-install workspace is deliberately not this: Desktop, Downloads and Documents are
    // recent-files there, being places things arrive rather than places things are kept.
    [TestMethod]
    public void TheDefaultWorkspaceStillOpensOnRecentFiles()
        => Assert.IsTrue(QuickPanelTab.CreateDefault().Folders.All(f => f.Kind == QuickPanelSourceKind.RecentFiles));

    [TestMethod]
    public void AddFolders_SkipsOneTheWorkspaceAlreadyHas()
    {
        var settings = BuildSettings(@"C:\a");
        var vm = new QuickPanelSettingsViewModel(settings);
        var tab = vm.Tabs.Single();

        tab.AddFolders(new[] { @"C:\A", @"C:\projects", @"C:\projects" });

        Assert.HasCount(2, tab.Sources);
    }
}
