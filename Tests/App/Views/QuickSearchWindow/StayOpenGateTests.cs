using System.Globalization;
using System.IO;
using System.Xml.Linq;

namespace Lertaro.App.Tests.Views.QuickSearchWindow;

// Inspect both logo declarations; parsing XML prevents comments from satisfying a markup contract.
[TestClass]
public sealed class StayOpenGateTests
{
    [TestMethod]
    public void BrandBlueHoverAndStayOpenNeverOverrideTheServiceWarning()
    {
        var styles = LoadSearchBox().Descendants().Where(e => e.Name.LocalName == "Style")
            .Where(e => e.Descendants().Any(t => (string?)t.Attribute("Binding") == "{Binding IsStayOpen, ElementName=root}")).ToList();
        Assert.HasCount(2, styles, "both logos need the indicator");
        foreach (var style in styles)
        {
            var triggers = style.Elements().Single(e => e.Name.LocalName == "Style.Triggers").Elements().ToList();
            var stayOpen = triggers.Single(e => (string?)e.Attribute("Binding") == "{Binding IsStayOpen, ElementName=root}");
            var serviceDown = triggers.Single(e => (string?)e.Attribute("Binding") == "{Binding IsServiceRunning, ElementName=root}");
            Assert.AreEqual("True", (string?)stayOpen.Attribute("Value"));
            Assert.AreEqual("False", (string?)serviceDown.Attribute("Value"));
            Assert.IsLessThan(triggers.IndexOf(serviceDown), triggers.IndexOf(stayOpen), "the last matching WPF trigger wins");

            // State is carried by the OUTLINE and the opacity, never by a fill: the ICO colors are preserved, so
            // the resting state is a transparent border and Stay Open is the accent colour rather than a brand
            // hex of its own. Both logos are expected to say exactly this.
            Assert.AreEqual("Transparent", ValueOf(style, "BorderBrush"));
            Assert.AreEqual(1.0, OpacityOf(style), "the logo is fully opaque at rest");
            Assert.AreEqual("{DynamicResource AccentBlue}", ValueOf(stayOpen, "BorderBrush"));
            Assert.AreEqual(1.0, OpacityOf(stayOpen));
            Assert.AreEqual("{DynamicResource WarningBrush}", ValueOf(serviceDown, "BorderBrush"));
            Assert.AreEqual(0.8, OpacityOf(serviceDown), "the warning dims the logo as well as recolouring the outline");

            var hover = style.Descendants().Single(e => e.Name.LocalName == "MultiDataTrigger");
            Assert.IsLessThan(triggers.IndexOf(serviceDown), triggers.IndexOf(hover), "hover must not hide the service warning");
            Assert.AreEqual("{DynamicResource AccentBlue}", ValueOf(hover, "BorderBrush"));
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

    /// <summary>The value a style or trigger gives one property, e.g. the outline a state paints.</summary>
    private static string? ValueOf(XElement owner, string property) =>
        (string?)owner.Elements().Single(e => (string?)e.Attribute("Property") == property).Attribute("Value");

    // Numeric, so "1" and "1.0" are the same statement about opacity rather than a spelling contest.
    private static double OpacityOf(XElement owner) => double.Parse(ValueOf(owner, "Opacity")!, CultureInfo.InvariantCulture);

    private static XDocument LoadSearchBox()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AGENTS.md"))) dir = dir.Parent;
        Assert.IsNotNull(dir, "could not locate the repository root");
        return XDocument.Load(Path.Combine(dir.FullName, "App/Views/Controls/SearchBoxControl.xaml"));
    }
}
