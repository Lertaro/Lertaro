using System.IO;
using System.Xml.Linq;

namespace Lertaro.App.Tests.Views.QuickSearchWindow;

// Inspect both logo declarations; parsing XML prevents comments from satisfying a markup contract.
[TestClass]
public sealed class StayOpenGateTests
{
    [TestMethod]
    public void TheIndicatorNeverOutranksTheServiceWarning()
    {
        var styles = LoadSearchBox().Descendants().Where(e => e.Name.LocalName == "Style")
            .Where(e => e.Descendants().Any(t => (string?)t.Attribute("Binding") == "{Binding IsStayOpen, ElementName=root}")).ToList();
        Assert.HasCount(2, styles, "both logos need the indicator");
        foreach (var style in styles)
        {
            var triggers = style.Descendants().Where(e => e.Name.LocalName == "DataTrigger").ToList();
            var stayOpen = triggers.Single(e => (string?)e.Attribute("Binding") == "{Binding IsStayOpen, ElementName=root}");
            var serviceDown = triggers.Single(e => (string?)e.Attribute("Binding") == "{Binding IsServiceRunning, ElementName=root}");
            Assert.AreEqual("True", (string?)stayOpen.Attribute("Value"));
            Assert.AreEqual("False", (string?)serviceDown.Attribute("Value"));
            Assert.IsLessThan(triggers.IndexOf(serviceDown), triggers.IndexOf(stayOpen), "the last matching WPF trigger wins");
            var indicator = stayOpen.Elements().Single(e => (string?)e.Attribute("Property") == "Background");
            var warning = serviceDown.Elements().Single(e => (string?)e.Attribute("Property") == "Background");
            Assert.AreEqual("{DynamicResource TextPrimary}", (string?)indicator.Attribute("Value"));
            Assert.AreEqual("{DynamicResource WarningBrush}", (string?)warning.Attribute("Value"));
        }
    }

    [TestMethod]
    public void BothLogoGridsDeclareTheMiddleClickHandler()
    {
        var logos = LoadSearchBox().Descendants().Where(e => (string?)e.Attribute("MouseUp") == "Icon_MouseUp").ToList();
        Assert.HasCount(2, logos);
        CollectionAssert.AreEquivalent(new[] { "0", "3" }, logos.Select(e => (string?)e.Attribute("Grid.Column")).ToArray());
        Assert.IsTrue(logos.All(e => e.Name.LocalName == "Grid"));
    }

    private static XDocument LoadSearchBox()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AGENTS.md"))) dir = dir.Parent;
        Assert.IsNotNull(dir, "could not locate the repository root");
        return XDocument.Load(Path.Combine(dir.FullName, "App/Views/Controls/SearchBoxControl.xaml"));
    }
}
