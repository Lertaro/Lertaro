using System.IO;
using System.Text.RegularExpressions;

namespace Lertaro.App.Tests.Views.Settings.Plugins;

// The Plugins settings page was reported as laggy, most visibly on its first open and on each click in
// the plugin list. The fixes for that are mostly structural (a virtualization setting, a lazy build, a
// timer's visibility gate), so a later edit can silently undo one of them without any behavioural test
// noticing -- the page still works, it is just slow again. These pin the invariants the same
// source-scanning way Tests/App/Views/QuickSearchWindow/StayOpenGateTests already pins its own.
[TestClass]
public sealed class PluginPagePerformanceInvariantTests
{
    [TestMethod]
    public void ThePluginListStaysVirtualized()
    {
        // ScrollViewer.CanContentScroll="False" is the trap: it forces physical scrolling, which turns
        // virtualization off, so every plugin row realizes its whole template up front and the list's
        // cost scales with the plugin count instead of with what is on screen.
        var page = Source("App/Views/Settings/Plugins/PluginManagementSettingsPage.xaml");
        var list = Between(page, "<ListBox x:Name=\"PluginsList\"", "</ListBox>");

        Assert.DoesNotContain("CanContentScroll=\"False\"", list,
            "the plugin list must not opt out of virtualization");
        Assert.Contains("VirtualizingPanel.IsVirtualizing=\"True\"", list, "virtualization must stay on");
        Assert.Contains("VirtualizingPanel.VirtualizationMode=\"Recycling\"", list,
            "recycling avoids rebuilding each realized row from scratch while scrolling");
    }

    [TestMethod]
    public void TheRuntimeStatusRowsAreBuiltLazily()
    {
        // Building one status VM per plugin (each reading a performance snapshot) in the constructor was
        // paid on every open of a tab that starts hidden.
        var vm = Source("App/ViewModels/Settings/Plugins/PluginManagementViewModel.cs");

        Assert.Contains("EnsureRuntimeStatusesBuilt", vm, "the lazy entry point is missing");
        Assert.DoesNotContain("RebuildRuntimeStatuses();\n        SaveConfigCommand", vm,
            "the constructor must not build runtime-status rows eagerly");
    }

    [TestMethod]
    public void TheRuntimeStatusTimerOnlyRunsWhileThePanelIsVisible()
    {
        // Loaded/Unloaded is wrong here: a collapsed element still raises Loaded, and collapsing never
        // raises Unloaded, so a Loaded-started timer kept ticking over the management tab.
        var view = Source("App/Views/Settings/Plugins/PluginRuntimeStatusView.xaml.cs");

        Assert.Contains("IsVisibleChanged", view, "the timer must be gated on actual visibility");
        Assert.DoesNotContain("Loaded += (_, _) => _refreshTimer.Start()", view,
            "starting on Loaded ran the timer while the panel was collapsed");
    }

    [TestMethod]
    public void TheDetailPaneSwapsDataContextRatherThanRecreatingTheCard()
    {
        // A ContentTemplate re-instantiates the whole PluginCard (three merged resource dictionaries,
        // nested ItemsControls, an eagerly built config section) on every selection.
        var page = Source("App/Views/Settings/Plugins/PluginManagementSettingsPage.xaml");

        Assert.Contains("<local:PluginCard Grid.Column=\"2\"", page, "the card should be a direct child");
        Assert.DoesNotContain("Content=\"{Binding SelectedPlugin}\"", page,
            "the detail pane must not rebuild the card through a ContentTemplate");
    }

    [TestMethod]
    public void TheComponentBadgeBrushesAreFrozen()
    {
        // Allocating a freezable per Convert, twice per group header, meant a fresh brush per header on
        // every card render.
        var converters = Source("App/Views/Settings/Plugins/PluginConverters.cs");
        var badge = Between(converters, "class ComponentTypeToBadgeBrushConverter", "class EmptyStringToPlaceholderConverter");

        Assert.Contains("brush.Freeze()", badge, "the badge brushes must be frozen for reuse");
        Assert.DoesNotContain("=> new SolidColorBrush(", badge,
            "each Convert call must not allocate a new brush");
    }

    [TestMethod]
    public void ConfigFieldChildrenAreBuiltLazily()
    {
        // Every plugin's whole config tree used to be constructed with the list, though only the one
        // open plugin's form is ever shown. The constructor must stay free of that load; the getters
        // are where it happens now.
        var field = Source("App/ViewModels/Settings/Plugins/PluginConfigFieldViewModel.cs");
        var loadSupport = Source("App/ViewModels/Settings/Plugins/PluginConfigFieldLoadSupport.cs");

        Assert.DoesNotContain("LoadChildrenAndArrayItems()", field,
            "the eager loader should be gone, replaced by the lazy getters");
        Assert.Contains("EnsureChildrenLoaded", loadSupport, "children must load through the lazy getter");
        Assert.Contains("EnsureArrayItemsLoaded", loadSupport, "array items must load through the lazy getter");
    }

    [TestMethod]
    public void PluginReflectionResultsAreCached()
    {
        // GetTypes() forces every type in the assembly to load and ran twice per plugin per list build.
        var helper = Source("App/Helpers/PluginLoaderHelper.cs");

        Assert.Contains("GetCachedTypes", helper, "the GetTypes() cache is missing");
        Assert.HasCount(0, Regex.Matches(helper, @"\bassembly\.GetTypes\(\)"),
            "every GetTypes() call should go through the cache");
    }

    private static string Between(string source, string from, string to)
    {
        var start = source.IndexOf(from, StringComparison.Ordinal);
        Assert.IsGreaterThan(-1, start, $"could not find '{from}'");
        var end = source.IndexOf(to, start + from.Length, StringComparison.Ordinal);
        Assert.IsGreaterThan(-1, end, $"could not find '{to}' after '{from}'");
        return source.Substring(start, end - start);
    }

    private static string Source(string relativePath)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AGENTS.md")))
            dir = dir.Parent;
        Assert.IsNotNull(dir, "could not locate the repository root");
        var path = Path.Combine(dir!.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.IsTrue(File.Exists(path), $"expected a file at {path}");
        return File.ReadAllText(path);
    }
}
