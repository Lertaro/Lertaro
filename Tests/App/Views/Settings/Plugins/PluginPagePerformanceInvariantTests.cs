using System.Globalization;
using System.IO;
using System.Windows.Media;
using System.Xml.Linq;
using Lertaro.App.Views.Settings.Plugins;
using Lertaro.App.ViewModels.Settings.Plugins;

namespace Lertaro.App.Tests.Views.Settings.Plugins;

[TestClass]
public sealed class PluginPagePerformanceInvariantTests
{
    [TestMethod]
    public void ThePluginListStaysVirtualized()
    {
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var list = LoadPage().Descendants().Single(e => (string?)e.Attribute(xaml + "Name") == "PluginsList");
        Assert.AreEqual("ListBox", list.Name.LocalName);
        Assert.AreNotEqual("False", (string?)list.Attribute("ScrollViewer.CanContentScroll"));
        Assert.AreEqual("True", (string?)list.Attribute("VirtualizingPanel.IsVirtualizing"));
        Assert.AreEqual("Pixel", (string?)list.Attribute("VirtualizingPanel.ScrollUnit"));
        Assert.AreEqual("Recycling", (string?)list.Attribute("VirtualizingPanel.VirtualizationMode"));
    }

    [TestMethod]
    public void TheDetailPaneSwapsDataContextRatherThanRecreatingTheCard()
    {
        var card = LoadPage().Descendants().Single(e => e.Name.LocalName == "PluginCard");
        Assert.AreEqual("Grid", card.Parent?.Name.LocalName);
        Assert.AreEqual("2", (string?)card.Attribute("Grid.Column"));
        Assert.AreEqual("{Binding SelectedPlugin}", (string?)card.Attribute("DataContext"));
    }

    [TestMethod]
    public void TheComponentBadgeBrushesAreFrozenAndReused()
    {
        var converter = new ComponentTypeToBadgeBrushConverter();
        foreach (var type in Enum.GetValues<PluginComponentType>())
        {
            var first = Assert.IsInstanceOfType<SolidColorBrush>(converter.Convert(type, typeof(Brush), null!, CultureInfo.InvariantCulture));
            var second = converter.Convert(type, typeof(Brush), null!, CultureInfo.InvariantCulture);
            Assert.IsTrue(first.IsFrozen, $"{type} must be safe to share");
            Assert.AreSame(first, second, $"{type} must not allocate a brush for each conversion");
        }
    }

    private static XDocument LoadPage()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AGENTS.md"))) dir = dir.Parent;
        Assert.IsNotNull(dir, "could not locate the repository root");
        return XDocument.Load(Path.Combine(dir.FullName, "App/Views/Settings/Plugins/PluginManagementSettingsPage.xaml"));
    }
}
