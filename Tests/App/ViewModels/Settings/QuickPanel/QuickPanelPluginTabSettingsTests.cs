using Lertaro.App.ViewModels.Settings.QuickPanel;
using Lertaro.Core;

namespace Lertaro.App.Tests.ViewModels.Settings.QuickPanel;

// The page-level list of plugin tabs. Which tabs exist comes from whatever plugins are loaded, which a
// test has none of -- so what is pinned here is what the page does with the state of the ones it cannot
// see, which is the part that can quietly destroy something.
[TestClass]
public sealed class QuickPanelPluginTabSettingsTests
{
    private static UserSettings BuildSettings(params string[] closedPluginTabs)
    {
        var tab = new QuickPanelTab { Id = "tab1" };
        tab.Folders.Add(QuickPanelFolderSource.For(@"C:\a"));

        var settings = new UserSettings();
        settings.QuickPanel.Tabs = new List<QuickPanelTab> { tab };
        settings.QuickPanel.ActiveTabId = tab.Id;
        settings.QuickPanel.ClosedPluginTabIds = closedPluginTabs.ToList();
        return settings;
    }

    // The tab of a plugin that is switched off has no row on this page, so saving must leave its closed
    // state alone. Rebuilt from the rows alone it would come back open the moment the plugin did -- and
    // the user closed it deliberately.
    [TestMethod]
    public void Save_KeepsTheClosedStateOfATabItCannotSee()
    {
        var settings = BuildSettings("Gone::QuickPanelTabProvider::Nothing");

        new QuickPanelSettingsViewModel(settings).Save();

        CollectionAssert.AreEqual(
            new[] { "Gone::QuickPanelTabProvider::Nothing" },
            settings.QuickPanel.ClosedPluginTabIds);
    }

    // Same rule for the display choice, and worth its own case: two lists are now rewritten from the
    // same rows, and getting one of them right says nothing about the other.
    [TestMethod]
    public void Save_KeepsTheDisplayChoiceOfATabItCannotSee()
    {
        var settings = BuildSettings();
        settings.QuickPanel.ListViewPluginTabIds = new List<string> { "Gone::QuickPanelTabProvider::Nothing" };

        new QuickPanelSettingsViewModel(settings).Save();

        CollectionAssert.AreEqual(
            new[] { "Gone::QuickPanelTabProvider::Nothing" },
            settings.QuickPanel.ListViewPluginTabIds);
    }

    // Tiles are what a panel of files is for, so a plugin tab starts on them: absence from the list is
    // what "tiles" means, exactly as absence from the closed list means "open".
    [TestMethod]
    public void APluginTabShowsTilesUntilItIsToldNotTo()
    {
        var settings = BuildSettings();

        new QuickPanelSettingsViewModel(settings).Save();

        Assert.IsEmpty(settings.QuickPanel.ListViewPluginTabIds);
    }

    [TestMethod]
    public void APluginTabIsOpenUntilItIsClosed()
    {
        // Nothing is closed, so nothing is listed: absence is what "open" means, which is what makes a
        // tab appear as soon as the plugin offering it does.
        var settings = BuildSettings();

        new QuickPanelSettingsViewModel(settings).Save();

        Assert.IsEmpty(settings.QuickPanel.ClosedPluginTabIds);
    }
}
