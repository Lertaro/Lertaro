using System.IO;
using System.Text.RegularExpressions;
using Lertaro.App.Helpers;

namespace Lertaro.App.Tests.Helpers;

// SettingsSearchIndex is hand-curated: nothing generates it from the pages, so a setting becomes
// searchable only if someone remembers to add a line for it. That is a step easy to skip, and skipping
// it fails silently -- the setting works, it just cannot be found from the search box, which nobody
// notices until they go looking for that one setting.
//
// The x:Name anchors in the settings XAML are the closest thing to a manifest of "rows a search result
// can point at", so these pin the index against them. Source-scanning rather than driving the real
// window, in the same spirit as Tests/App/Views/QuickSearchWindow/StayOpenGateTests: the reveal itself
// needs a live visual tree, but the bookkeeping that feeds it does not.
[TestClass]
public sealed class SettingsSearchIndexTests
{
    [TestMethod]
    public void EverySettingsRowAnchorIsReachableFromTheSearchIndex()
    {
        var indexed = IndexedAnchors();
        var missing = new List<string>();

        foreach (var file in SettingsXamlFiles())
        {
            foreach (Match match in Regex.Matches(File.ReadAllText(file), @"x:Name=""(Row[A-Za-z0-9_]*)"""))
            {
                var anchor = match.Groups[1].Value;
                if (!indexed.Contains(anchor))
                    missing.Add($"{anchor} ({Path.GetFileName(file)})");
            }
        }

        // A Row anchor exists so a search result can land on it. If one is genuinely not a searchable
        // setting, the fix is to drop the anchor rather than to leave it looking indexed.
        Assert.IsEmpty(missing,
            "these settings rows are anchored in XAML but no index entry points at them, so they cannot "
            + "be found from the settings search box: " + string.Join(", ", missing));
    }

    [TestMethod]
    public void NoIndexEntryPointsAtAnAnchorThatNoLongerExists()
    {
        var anchors = SettingsXamlFiles()
            .SelectMany(f => Regex.Matches(File.ReadAllText(f), @"x:Name=""(Row[A-Za-z0-9_]*)""")
                .Select(m => m.Groups[1].Value))
            .ToHashSet(StringComparer.Ordinal);

        // The other direction, and the quieter failure of the two: a renamed or deleted row leaves an
        // entry that still matches the query and still switches sections, then reveals nothing at all.
        var dangling = IndexedAnchors().Where(a => !anchors.Contains(a)).ToList();

        Assert.IsEmpty(dangling,
            "these index entries name a row that no settings page declares any more: "
            + string.Join(", ", dangling));
    }

    [TestMethod]
    public void TheQuickWindowPositionLockIsSearchable()
    {
        // Regression: the lock shipped with its x:Name in place but no index line, so every other route
        // to it worked and only the search box came up empty.
        var entry = SettingsSearchIndex.Entries.SingleOrDefault(e => e.LabelKey == "General_LayoutLockPosition");

        Assert.IsNotNull(entry, "the position lock has no search index entry");
        Assert.AreEqual("General", entry!.Section);
        Assert.AreEqual("TabLayout/RowLayoutLockPosition", entry.TargetElementName);
    }

    [TestMethod]
    public void TheFullWindowSingleInstanceSettingIsSearchable()
    {
        var entry = SettingsSearchIndex.Entries.SingleOrDefault(e => e.LabelKey == "General_SearchWindowSingleInstance");

        Assert.IsNotNull(entry, "the full-window single-instance setting has no search index entry");
        Assert.AreEqual("TabSearchWindow/RowSearchWindowSingleInstance", entry!.TargetElementName);
    }

    [TestMethod]
    public void TheExplorerTabsSettingIsSearchable()
    {
        var entry = SettingsSearchIndex.Entries.SingleOrDefault(e => e.LabelKey == "General_OpenFoldersInNewExplorerTabs");

        Assert.IsNotNull(entry, "the Explorer-tabs setting has no search index entry");
        Assert.AreEqual("TabSystem/RowOpenFoldersInNewExplorerTabs", entry!.TargetElementName);
    }

    [TestMethod]
    public void ThePluginRuntimeStatusTabIsSearchable()
    {
        var entry = SettingsSearchIndex.Entries.SingleOrDefault(e => e.LabelKey == "Plugins_TabRuntimeStatus");

        Assert.IsNotNull(entry, "the plugin runtime status tab has no search index entry");
        Assert.AreEqual("Plugins", entry!.Section);
        Assert.AreEqual("TabRuntimeStatus", entry.TargetElementName);
        Assert.IsNotNull(entry.Activate, "the plugin runtime status tab does not select itself");
    }

    [TestMethod]
    public void ThePluginManagementTabIsSearchable()
    {
        var entry = SettingsSearchIndex.Entries.SingleOrDefault(e => e.LabelKey == "Plugins_TabManagement");

        Assert.IsNotNull(entry, "the plugin management tab has no search index entry");
        Assert.AreEqual("Plugins", entry!.Section);
        Assert.AreEqual("TabManagement", entry.TargetElementName);
        Assert.IsNotNull(entry.Activate, "the plugin management tab does not select itself");
    }

    [TestMethod]
    public void TheStayOpenHotkeyIsSearchable()
    {
        var entry = SettingsSearchIndex.Entries.SingleOrDefault(e => e.LabelKey == "Hotkeys_StayOpen");

        Assert.IsNotNull(entry, "the stay-open hotkey has no search index entry");
        Assert.AreEqual("Hotkeys", entry!.Section);
        Assert.AreEqual("TabGlobal/RowStayOpen", entry.TargetElementName);
    }

    [TestMethod]
    public void TheQuickLaunchItemsTitleSelectsTheItemsSubsection()
    {
        var entry = SettingsSearchIndex.Entries.SingleOrDefault(e => e.LabelKey == "QuickLaunch_ItemsTitle");

        Assert.IsNotNull(entry, "the quick-launch items title has no search index entry");
        Assert.IsNotNull(entry!.Activate, "the quick-launch items title does not select its subsection");
        Assert.AreEqual("RowQuickLaunchItemsTitle", entry.TargetElementName);
    }

    [TestMethod]
    public void TheQuickLaunchSourceTitleSelectsTheSourcesSubsection()
    {
        var entry = SettingsSearchIndex.Entries.SingleOrDefault(e => e.LabelKey == "QuickLaunch_SourceTitle");

        Assert.IsNotNull(entry, "the quick-launch source title has no search index entry");
        Assert.IsNotNull(entry!.Activate, "the quick-launch source title does not select its subsection");
        Assert.AreEqual("RowQuickLaunchSourceTitle", entry.TargetElementName);
    }

    // Every segment of every reveal path. The paths are "outer/inner" hops resolved one FindName at a
    // time (see SettingsSearchEntry), so a nested anchor only ever appears as a trailing segment.
    private static HashSet<string> IndexedAnchors() =>
        SettingsSearchIndex.Entries
            .Where(e => e.TargetElementName != null)
            .SelectMany(e => e.TargetElementName!.Split('/'))
            .Where(s => s.StartsWith("Row", StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);

    private static IEnumerable<string> SettingsXamlFiles()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AGENTS.md")))
            dir = dir.Parent;
        Assert.IsNotNull(dir, "could not locate the repository root");

        var settings = Path.Combine(dir!.FullName, "App", "Views", "Settings");
        Assert.IsTrue(Directory.Exists(settings), $"expected the settings pages at {settings}");
        return Directory.EnumerateFiles(settings, "*.xaml", SearchOption.AllDirectories);
    }
}
