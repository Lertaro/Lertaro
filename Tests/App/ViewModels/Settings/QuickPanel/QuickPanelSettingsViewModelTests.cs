using Lertaro.App.ViewModels.Settings.QuickPanel;
using Lertaro.Core;

namespace Lertaro.App.Tests.ViewModels.Settings.QuickPanel;

// Covers the staging both view models do: what the source list on screen turns into in the settings
// file. AddFolder is the one command left out -- it opens a folder picker and has nothing else in it.
[TestClass]
public sealed class QuickPanelSettingsViewModelTests
{
    // QuickPanelSettings ships with a default tab; these tests replace it with one holding exactly the
    // folders each case needs.
    private static UserSettings BuildSettings(params string[] folderPaths)
    {
        var tab = new QuickPanelTab { Id = "tab1" };
        foreach (var path in folderPaths)
            tab.Folders.Add(QuickPanelFolderSource.For(path));

        var settings = new UserSettings();
        settings.QuickPanel.Tabs = new List<QuickPanelTab> { tab };
        settings.QuickPanel.ActiveTabId = tab.Id;
        return settings;
    }

    [TestMethod]
    public void Save_WritesTheListOrderAndTheHiddenRows()
    {
        var settings = BuildSettings(@"C:\a", @"C:\b");
        var vm = new QuickPanelSettingsViewModel(settings);
        var tab = vm.Tabs.Single();
        var first = tab.Sources[0];
        tab.Sources[1].IsVisible = false;
        tab.MoveDownCommand.Execute(first);

        vm.Save();

        var saved = settings.QuickPanel.Tabs.Single();
        // The row that moved down is no longer first, and the order stored is the order on screen.
        Assert.AreNotEqual(first.Id, saved.GroupOrder[0]);
        CollectionAssert.AreEqual(tab.Sources.Select(s => s.Id).ToList(), saved.GroupOrder);
        CollectionAssert.AreEqual(new[] { tab.Sources.First(s => !s.IsVisible).Id }, saved.DisabledGroupIds);
    }

    [TestMethod]
    public void Save_CustomName_IsStoredOnlyWhenThereIsOne()
    {
        var settings = BuildSettings(@"C:\a", @"C:\b");
        var vm = new QuickPanelSettingsViewModel(settings);
        var tab = vm.Tabs.Single();
        var named = tab.Sources[0];
        named.DisplayName = "  素材  ";

        vm.Save();

        var saved = settings.QuickPanel.Tabs.Single();
        Assert.AreEqual("素材", saved.GroupPreferences[named.Id].DisplayName);
        Assert.IsFalse(saved.GroupPreferences.ContainsKey(tab.Sources[1].Id), "an untouched row needs no entry");
    }

    // Stored inverted, as the preference's ThumbnailView: tiles are what a group starts as, so the box
    // on screen is the one that turns them off.
    [TestMethod]
    public void Save_ShowAsList_RoundTripsAsTheInverseOfThumbnailView()
    {
        var settings = BuildSettings(@"C:\a", @"C:\b");
        var vm = new QuickPanelSettingsViewModel(settings);
        var listed = vm.Tabs.Single().Sources[0];
        listed.ShowAsList = true;

        vm.Save();

        var saved = settings.QuickPanel.Tabs.Single();
        Assert.IsFalse(saved.GroupPreferences[listed.Id].ThumbnailView);
        Assert.IsFalse(saved.GroupPreferences.ContainsKey(vm.Tabs.Single().Sources[1].Id),
            "a row left on thumbnails has overridden nothing and needs no entry");

        // And back: reopening the page shows the box the way it was left.
        Assert.IsTrue(new QuickPanelSettingsViewModel(settings).Tabs.Single().Sources[0].ShowAsList);
    }

    // The name is not what makes a preference worth storing any more, so unchecking the box on a row
    // that was only ever renamed must not undo the rename.
    [TestMethod]
    public void Save_ShowAsList_AndACustomName_BothLandOnTheSameEntry()
    {
        var settings = BuildSettings(@"C:\a");
        var vm = new QuickPanelSettingsViewModel(settings);
        var row = vm.Tabs.Single().Sources[0];
        row.DisplayName = "素材";
        row.ShowAsList = true;

        vm.Save();

        var preference = settings.QuickPanel.Tabs.Single().GroupPreferences[row.Id];
        Assert.AreEqual("素材", preference.DisplayName);
        Assert.IsFalse(preference.ThumbnailView);
    }

    [TestMethod]
    public void Save_RemovedFolder_LeavesNothingBehind()
    {
        var settings = BuildSettings(@"C:\a", @"C:\b");
        var vm = new QuickPanelSettingsViewModel(settings);
        var tab = vm.Tabs.Single();
        var doomed = tab.Sources[0];
        doomed.DisplayName = "gone";
        tab.RemoveSourceCommand.Execute(doomed);

        vm.Save();

        var saved = settings.QuickPanel.Tabs.Single();
        Assert.IsFalse(saved.Folders.Any(f => f.Id == doomed.Id));
        CollectionAssert.DoesNotContain(saved.GroupOrder, doomed.Id);
        Assert.IsFalse(saved.GroupPreferences.ContainsKey(doomed.Id), "a deleted source must not keep accumulating");
    }

    [TestMethod]
    public void Save_FolderFields_RoundTrip()
    {
        var settings = BuildSettings(@"C:\a");
        var vm = new QuickPanelSettingsViewModel(settings);
        var row = vm.Tabs.Single().Sources.First();
        row.Kind = QuickPanelSourceKind.All;
        row.Recursive = true;
        row.FilterPattern = " *.exe;*.lnk ";
        row.MaxItems = 50;
        row.AcceptsDrops = true;

        vm.Save();

        var folder = settings.QuickPanel.Tabs.Single().Folders.Single();
        Assert.AreEqual(QuickPanelSourceKind.All, folder.Kind);
        Assert.IsTrue(folder.Recursive);
        Assert.AreEqual("*.exe;*.lnk", folder.FilterPattern);
        Assert.AreEqual(50, folder.MaxItems);
        Assert.IsTrue(folder.AcceptsDrops);

        // And back: reopening the page shows the box the way it was left.
        Assert.IsTrue(new QuickPanelSettingsViewModel(settings).Tabs.Single().Sources.Single().AcceptsDrops);
    }

    [TestMethod]
    public void FolderFilter_EmptyMeansNoFilter()
    {
        var settings = BuildSettings(@"C:\a");
        var vm = new QuickPanelSettingsViewModel(settings);
        var row = vm.Tabs.Single().Sources.Single();

        Assert.AreEqual(string.Empty, row.FilterPattern);

        row.FilterPattern = "   ";
        vm.Save();

        Assert.AreEqual(string.Empty, settings.QuickPanel.Tabs.Single().Folders.Single().FilterPattern);
    }

    // The clone gets its own folder ids, so everything that addresses a source by id has to be rewritten
    // with it -- otherwise the copy's order and hidden list would point at the original's sources.
    [TestMethod]
    public void DuplicateTab_RemapsEverythingThatAddressesASourceById()
    {
        var settings = BuildSettings(@"C:\a", @"C:\b");
        var vm = new QuickPanelSettingsViewModel(settings);
        var original = vm.Tabs.Single();
        original.Sources[0].DisplayName = "素材";
        original.Sources[1].IsVisible = false;

        vm.DuplicateTabCommand.Execute(null);
        vm.Save();

        var copy = settings.QuickPanel.Tabs.Last();
        var originalIds = settings.QuickPanel.Tabs.First().Folders.Select(f => f.Id).ToHashSet();
        Assert.HasCount(2, copy.Folders);
        Assert.IsFalse(copy.Folders.Any(f => originalIds.Contains(f.Id)), "a clone must not reuse ids");
        Assert.IsFalse(copy.GroupOrder.Any(originalIds.Contains), "order still points at the original's sources");
        Assert.IsFalse(copy.DisabledGroupIds.Any(originalIds.Contains), "hidden list still points at the original's sources");
        Assert.IsTrue(copy.GroupPreferences.Values.Any(p => p.DisplayName == "素材"));
        Assert.IsFalse(copy.GroupPreferences.Keys.Any(originalIds.Contains));
    }

    // Each row carries its own reorder/delete buttons, so the commands they bind to have to exist by
    // the time the row does -- a null one is a button that silently does nothing when clicked.
    [TestMethod]
    public void RowCommands_AreBoundOnEveryTab_IncludingOnesAddedLater()
    {
        var settings = BuildSettings(@"C:\a");
        var vm = new QuickPanelSettingsViewModel(settings);
        vm.AddTabCommand.Execute(null);
        vm.DuplicateTabCommand.Execute(null);

        Assert.IsGreaterThanOrEqualTo(3, vm.Tabs.Count);
        foreach (var tab in vm.Tabs)
        {
            Assert.IsNotNull(tab.MoveUpSelfCommand, tab.EffectiveName);
            Assert.IsNotNull(tab.MoveDownSelfCommand, tab.EffectiveName);
            Assert.IsNotNull(tab.RemoveSelfCommand, tab.EffectiveName);
        }
    }

    [TestMethod]
    public void RowRemoveCommand_RemovesThatRowRatherThanTheSelectedOne()
    {
        var settings = BuildSettings(@"C:\a");
        var vm = new QuickPanelSettingsViewModel(settings);
        vm.AddTabCommand.Execute(null);   // selects the new one
        var first = vm.Tabs[0];

        first.RemoveSelfCommand.Execute(first);

        CollectionAssert.DoesNotContain(vm.Tabs.ToList(), first);
    }

    // The dropdown binds SelectedValue to Kind against these options: a kind missing from the list is a
    // source whose type the box cannot display, which is what an empty dropdown looked like.
    [TestMethod]
    public void KindOptions_OfferEveryKind_WithDistinctValues()
    {
        var settings = BuildSettings(@"C:\a");
        var vm = new QuickPanelSettingsViewModel(settings);
        var row = vm.Tabs.Single().Sources.First();

        var values = row.KindOptions.Select(o => o.Value).ToList();

        var expected = new[] { QuickPanelSourceKind.RecentFiles, QuickPanelSourceKind.All, QuickPanelSourceKind.FoldersOnly, QuickPanelSourceKind.FilesOnly };
        CollectionAssert.AreEquivalent(expected, values);
        Assert.IsTrue(row.KindOptions.All(o => !string.IsNullOrWhiteSpace(o.Label)));
    }

    [TestMethod]
    public void Save_WorkspaceEnabledFlag_RoundTrips()
    {
        var settings = BuildSettings(@"C:\a");
        var vm = new QuickPanelSettingsViewModel(settings);
        vm.Tabs.Single().Enabled = false;

        vm.Save();

        Assert.IsFalse(settings.QuickPanel.Tabs.Single().Enabled);
    }

    // Everything on this page stages until Save, like every other settings page. The view models edit
    // clones for that reason: the originals live inside the process-wide UserSettings, so touching them
    // would make each edit instantly live and immune to Cancel.
    [TestMethod]
    public void Edits_DoNotReachUserSettingsUntilSave()
    {
        var settings = BuildSettings(@"C:\a", @"C:\b");
        var vm = new QuickPanelSettingsViewModel(settings);
        var tab = vm.Tabs.Single();
        tab.Name = "renamed";
        tab.Enabled = false;
        tab.Sources.First().DisplayName = "素材";
        tab.RemoveSourceCommand.Execute(tab.Sources.First());
        vm.AddTabCommand.Execute(null);

        var live = settings.QuickPanel.Tabs.Single();
        Assert.HasCount(1, settings.QuickPanel.Tabs, "a workspace was added before Save");
        Assert.IsEmpty(live.Name);
        Assert.IsTrue(live.Enabled);
        Assert.HasCount(2, live.Folders, "a source was removed before Save");
        Assert.IsEmpty(live.GroupPreferences);

        vm.Save();

        Assert.HasCount(2, settings.QuickPanel.Tabs);
        Assert.AreEqual("renamed", settings.QuickPanel.Tabs[0].Name);
        Assert.IsFalse(settings.QuickPanel.Tabs[0].Enabled);
        Assert.HasCount(1, settings.QuickPanel.Tabs[0].Folders);
    }

    // Deleting the last workspace is allowed: an empty list is a legitimate state, and refusing it to
    // avoid one would be the page deciding for the user.
    [TestMethod]
    public void RemoveTab_LastRemainingTab_IsRemoved()
    {
        var settings = BuildSettings(@"C:\a");
        var vm = new QuickPanelSettingsViewModel(settings);

        vm.RemoveTabCommand.Execute(null);
        vm.Save();

        Assert.IsEmpty(vm.Tabs);
        Assert.IsNull(vm.SelectedTab);
        Assert.IsEmpty(settings.QuickPanel.Tabs);
    }
}
