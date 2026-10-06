using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Lertaro.App.ViewModels.Settings.Plugins;
using Lertaro.App.Views.Settings.Plugins;
using Lertaro.Core;
using Lertaro.PluginSdk.Abstractions;

namespace Lertaro.App.Tests.Views.Settings;

[TestClass]
public sealed class PluginCustomPanelTests
{
    [StaTestMethod]
    [DataRow(ConfigFieldType.Group)]
    [DataRow(ConfigFieldType.Object)]
    public void CustomPanel_InContainer_RemainsInVisibleLayout(ConfigFieldType containerType)
    {
        var editor = new TextBox { Text = "dictionary.mdx" };
        var panel = new UserControl { Content = editor };
        var container = new PluginConfigField
        {
            Key = "MDict", LabelKey = "MDict", FieldType = containerType,
            SubFields = [new PluginConfigField
            {
                Key = "MDict.CustomPanel", LabelKey = "MDict", FieldType = ConfigFieldType.CustomControl,
                CustomControl = panel
            }]
        };
        var field = new PluginConfigFieldViewModel("flow", container, new UserSettings());
        var plugin = new PluginInfoViewModel("Flow", "1.0", "Flow.dll", "1.0", [], [field]);
        plugin.IsConfigTab = true;
        var section = new PluginConfigSection { DataContext = plugin };
        section.Measure(new Size(750, 600));
        section.Arrange(new Rect(0, 0, 750, 600));
        section.UpdateLayout();

        Assert.IsGreaterThan(0, editor.ActualHeight);
        DependencyObject? current = panel;
        while (current != null && !ReferenceEquals(current, section))
        {
            if (current is UIElement element)
                Assert.AreEqual(Visibility.Visible, element.Visibility,
                    $"The custom panel was attached under hidden {element.GetType().Name}.");
            current = VisualTreeHelper.GetParent(current);
        }
        Assert.AreSame(section, current, "The custom panel must belong to the displayed settings section.");
        Assert.AreEqual("dictionary.mdx", editor.Text);
    }
}
