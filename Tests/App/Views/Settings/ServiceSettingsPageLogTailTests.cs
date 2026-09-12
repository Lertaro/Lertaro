using System.IO;
using Lertaro.App.Views.Settings;

namespace Lertaro.App.Tests.Views.Settings;

// The log page's live tail used to rebuild its whole FlowDocument on the log timer's every tick (2s),
// measuring each line with FormattedText, even while the page was not the one on screen -- a 50-150ms
// UI-thread stall twice a second that made the rest of the settings window feel laggy. Two things have
// to hold for that to stay fixed: refreshes arriving off-screen must not be rendered, and only the few
// heaviest lines must be measured rather than all of them.
[TestClass]
public sealed class ServiceSettingsPageLogTailTests
{
    [TestMethod]
    public void WeightedLength_CountsFullWidthCharactersTwice()
    {
        // The log is monospaced: a CJK/full-width glyph takes twice the advance of a Latin one, so this
        // weighting is what lets the widest line be picked without measuring every line.
        Assert.AreEqual(3, ServiceSettingsPage.WeightedLength("abc"));
        Assert.AreEqual(8, ServiceSettingsPage.WeightedLength("中文测试"));
        Assert.AreEqual(6, ServiceSettingsPage.WeightedLength("ab中文"));
    }

    [TestMethod]
    public void WeightedLength_RanksByRenderedWidthNotCharacterCount()
    {
        // Six CJK characters (weight 12) render wider than ten Latin ones (weight 10), even though the
        // Latin string has more characters. String.Length would rank them the other way round.
        var latin = new string('a', 10);
        var cjk = new string('中', 6);

        Assert.IsGreaterThan(ServiceSettingsPage.WeightedLength(latin), ServiceSettingsPage.WeightedLength(cjk));
    }

    [TestMethod]
    public void ARereshOffScreenIsRecordedButNotRendered()
    {
        // Rendering is deferred behind two gates: the page has to be the one on screen, and then the UI
        // has to go idle (see RequestRebuild). A refresh arriving while another tab shows only records the
        // lines.
        var page = Source("App/Views/Settings/ServiceSettingsPage.xaml.cs");

        var onChanged = Between(page, "private void OnLogLinesChanged", "private void RequestRebuild");
        Assert.Contains("_pendingLines = lines", onChanged, "the refresh must be recorded");
        Assert.DoesNotContain("RebuildLogDocument(", onChanged,
            "nothing may be rendered from the off-screen path");

        Assert.Contains("IsVisibleChanged", page, "the deferred rebuild must run when the page is shown");
    }

    [TestMethod]
    public void TheRebuildIsDeferredToIdleRatherThanRunOnTheFramesTheUserWaitsOn()
    {
        // The whole cost is the RichTextBox laying out its FlowDocument natively (a CPU profile shows it
        // entirely in unsymbolized frames), so it can only be kept off the frames the user is waiting on,
        // not made cheaper. Background priority runs it after the current layout/render -- so the settings
        // window opens (and a click made straight after it is handled) without paying for the log.
        var page = Source("App/Views/Settings/ServiceSettingsPage.xaml.cs");
        var request = Between(page, "private void RequestRebuild()", "private void RebuildNow()");

        Assert.Contains("DispatcherPriority.Background", request,
            "the rebuild must be queued below the render pass, or it lands on the opening frame");
        Assert.Contains("Dispatcher.BeginInvoke", request, "queued, never run inline");
        Assert.DoesNotContain("RebuildLogDocument(", request,
            "the request itself must not render anything");
        Assert.Contains("_rebuildQueued", request, "repeated refreshes must coalesce into one rebuild");
    }

    [TestMethod]
    public void OnlyTheHeaviestLinesAreMeasured()
    {
        // FormattedText per line was the bulk of the rebuild's cost; the fix measures the heaviest few.
        var page = Source("App/Views/Settings/ServiceSettingsPage.xaml.cs");
        var rebuild = Between(page, "private void RebuildLogDocument", "private const int MeasuredCandidates");

        Assert.Contains("OrderByDescending", rebuild, "candidates must be ranked by weighted width");
        Assert.Contains("Take(MeasuredCandidates)", rebuild, "and only the heaviest few measured");
        Assert.DoesNotContain("widths.Add(MeasureWidth", rebuild, "no per-line measurement loop may come back");
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
        return File.ReadAllText(Path.Combine(dir!.FullName, relativePath.Replace('/', Path.DirectorySeparatorChar)));
    }
}
