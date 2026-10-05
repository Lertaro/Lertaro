using System.IO;
using System.Xml.Linq;

namespace Lertaro.App.Tests.ViewModels.Search;

// Markup contracts only. Geometry and result ordering are exercised by their behavioral tests.
[TestClass]
public sealed class InlineSearchStaysOneSearchTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [TestMethod]
    public void TheInlineCardHasNoUserResizeHandle()
    {
        var window = LoadWindow();
        Assert.IsFalse(window.Descendants().Any(e => e.Name.LocalName == "ResizeGrip"));
    }

    [TestMethod]
    public void TheInlinePathBannerUsesNaturalHeightForTheCompletePath()
    {
        var window = LoadWindow();
        var banner = window.Descendants().Single(e => (string?)e.Attribute(Xaml + "Name") == "PathPreviewBorder");
        var text = banner.Descendants().Single(e => (string?)e.Attribute(Xaml + "Name") == "PathPreviewTextBlock");
        var results = window.Descendants().Single(e => (string?)e.Attribute(Xaml + "Name") == "ResultsContainerWrapper");

        Assert.IsNull(banner.Attribute("Height"));
        Assert.IsNull(banner.Attribute("MinHeight"));
        Assert.AreEqual("Wrap", (string?)text.Attribute("TextWrapping"));
        Assert.IsNull(text.Attribute("TextTrimming"));
        Assert.AreSame(banner.Parent, results.Parent);
        Assert.AreEqual("0", (string?)banner.Attribute("Grid.Row"));
        Assert.AreEqual("1", (string?)results.Attribute("Grid.Row"));
    }

    [TestMethod]
    public void TheInlineResultsControlUsesInlineActionRows()
    {
        var control = LoadWindow().Descendants().Single(e => (string?)e.Attribute(Xaml + "Name") == "ResultsPanelControl");
        Assert.AreEqual("True", (string?)control.Attribute("UseInlineActionRows"));
    }

    private static XDocument LoadWindow()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AGENTS.md"))) dir = dir.Parent;
        Assert.IsNotNull(dir, "could not locate the repository root");
        return XDocument.Load(Path.Combine(dir.FullName, "App/Views/InlineSearchWindow/InlineSearchWindow.xaml"));
    }
}
