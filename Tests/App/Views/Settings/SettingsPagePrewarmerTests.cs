using System.IO;
using System.Text.RegularExpressions;

namespace Lertaro.App.Tests.Views.Settings;

// Every settings tab should be warm by the time the user clicks it, so the prewarmer walks the sidebar and
// builds one page per idle slot. Two things about that walk are easy to get subtly wrong and would fail
// silently (a tab simply stays slow): an ordering rule that stops covering everything, and a section the
// window has no page for being skipped without anyone noticing.
[TestClass]
public sealed class SettingsPagePrewarmerTests
{
    [TestMethod]
    public void ThePrewarmerCoversEverySectionTheSidebarOffers()
    {
        // The tabs come from the window's own sidebar, so a scrolled-in new tab is covered automatically --
        // but only as long as it is read from there rather than from a second list that can drift.
        var prewarmer = Source("App/Views/Settings/SettingsPagePrewarmer.cs");

        Assert.Contains("LstSections.Items", prewarmer, "the main sidebar is the source of the order");
        Assert.Contains("LstSectionsBottom.Items", prewarmer, "and so is the bottom-docked list (About)");

        // And each of those tags must resolve to a page, or the tab would be prewarmed into nothing.
        var mapped = Regex.Matches(Source("App/Views/Settings/SettingsWindowSearchActivationHelper.cs"),
                @"""(?<tag>[A-Za-z]+)"" => window\.Page")
            .Select(m => m.Groups["tag"].Value)
            .ToHashSet(StringComparer.Ordinal);

        var sidebarTags = Regex.Matches(Source("App/Views/Settings/SettingsWindow.xaml"), @"ListBoxItem Tag=""(?<tag>[A-Za-z]+)""")
            .Select(m => m.Groups["tag"].Value)
            .ToList();

        Assert.IsNotEmpty(sidebarTags, "expected the sidebar to declare its tabs");
        foreach (var tag in sidebarTags)
            Assert.Contains(tag, mapped, $"the '{tag}' tab has no page, so prewarming it would do nothing");
    }

    [TestMethod]
    public void TheHeaviestPageIsBuiltLast()
    {
        // Plugins reflects over every loaded plugin assembly, far more than any other page. Building it last
        // lets the cheap tabs warm first; if the user gets there first they take the ordinary on-demand path.
        var prewarmer = Source("App/Views/Settings/SettingsPagePrewarmer.cs");

        Assert.Contains("HeaviestSection", prewarmer, "the ordering rule should name the heavy section");
        Assert.Contains("\"Plugins\"", prewarmer, "and it is Plugins");
        Assert.Contains("OrderBy", prewarmer, "the rule must actually reorder the walk");
    }

    [TestMethod]
    public void PrewarmingIsIdleOnlyAndOnePageAtATime()
    {
        // Idle priority is what keeps this from competing with the user, and one page per callback bounds
        // the work in any single slot. A loop here (or a tighter priority) would turn a background nicety
        // into the very stall it exists to remove.
        var prewarmer = Source("App/Views/Settings/SettingsPagePrewarmer.cs");

        Assert.Contains("DispatcherPriority.ApplicationIdle", prewarmer,
            "prewarming must only run when the UI thread is otherwise idle");
        Assert.HasCount(1, Regex.Matches(prewarmer, @"GetSectionPage\("),
            "each idle callback must build exactly one page");
        Assert.Contains("Dispatcher.BeginInvoke", prewarmer,
            "the next page must be queued, leaving the dispatcher free in between");
    }

    [TestMethod]
    public void PrewarmingActuallyBuildsTheVisualTreeNotJustTheObjects()
    {
        // Constructing a page is NOT enough: pages are parented Collapsed (SettingsWindow.AddPage), and
        // WPF skips measure/arrange entirely for a Collapsed element, so the page's C# objects exist but
        // its visual tree does not -- it all got realized on the first click instead. Measured on the
        // plugin page that was ~211ms of the click; briefly making the page Hidden (which DOES participate
        // in layout) and running an explicit layout pass drops the first switch to ~2ms.
        //
        // A regression here is silent by nature -- the prewarm still "runs", every test above still passes,
        // and the tab is just slow again -- so this pins the two load-bearing steps.
        var prewarmer = Source("App/Views/Settings/SettingsPagePrewarmer.cs");

        Assert.Contains("Visibility.Hidden", prewarmer,
            "the page must be Hidden (not left Collapsed) so it takes part in the layout pass");
        Assert.Contains("UpdateLayout()", prewarmer,
            "and a layout pass must actually run to build the tree");
        Assert.Contains("Visibility.Collapsed", prewarmer,
            "afterwards the page goes back to Collapsed, its resting state until shown");

        // Hidden must come BEFORE the layout call, and Collapsed after it -- otherwise the pass either
        // skips the page or the page is left painted rather than hidden.
        var hiddenAt = prewarmer.IndexOf("Visibility.Hidden", StringComparison.Ordinal);
        var layoutAt = prewarmer.IndexOf("UpdateLayout()", StringComparison.Ordinal);
        var collapsedAfter = prewarmer.IndexOf("Visibility.Collapsed", hiddenAt, StringComparison.Ordinal);
        Assert.IsLessThan(layoutAt, hiddenAt, "the page must be made Hidden before layout runs");
        Assert.IsLessThan(collapsedAfter, layoutAt, "and returned to Collapsed only after it has run");

        // The page the user is currently looking at must be left alone -- flipping IT would repaint it.
        Assert.Contains("Visibility.Visible", prewarmer,
            "a page that is already Visible must be skipped");
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
