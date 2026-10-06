using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using Lertaro.PluginSdk.Windows;

namespace Lertaro.App.Tests.Helpers.Visuals;

[TestClass]
[DoNotParallelize]
public sealed class AppTextRenderingTests
{
    [StaTestMethod]
    public void StartupDefaultReachesWindowsPopupsAndPluginContent()
    {
        RuntimeHelpers.RunClassConstructor(typeof(Lertaro.App.App).TypeHandle);
        var text = new TextBlock(new Run("中文 日本語 한국어 العربية हिन्दी English"));
        var window = new Window { Content = text };
        var popup = new Popup { Child = new TextBox() };
        var pluginWindow = new PluginWindow("Rendering check");
        var pluginText = new TextBlock();
        pluginWindow.ContentHostControl.Content = pluginText;

        try
        {
            foreach (var element in new DependencyObject[] { window, text, text.Inlines.FirstInline, popup.Child, pluginText })
                Assert.AreEqual(TextFormattingMode.Display, TextOptions.GetTextFormattingMode(element), element.GetType().FullName);
        }
        finally
        {
            pluginWindow.Close();
            window.Close();
        }
    }

    [StaTestMethod]
    public void ExplicitFormattingModeCanStillOverrideTheAppDefault()
    {
        RuntimeHelpers.RunClassConstructor(typeof(Lertaro.App.App).TypeHandle);
        var run = new Run("Explicit formatting");
        var text = new TextBlock(run);
        var parent = new Border { Child = text };
        TextOptions.SetTextFormattingMode(parent, TextFormattingMode.Ideal);

        Assert.AreEqual(TextFormattingMode.Ideal, TextOptions.GetTextFormattingMode(text));
        Assert.AreEqual(TextFormattingMode.Ideal, TextOptions.GetTextFormattingMode(run));
    }
}
