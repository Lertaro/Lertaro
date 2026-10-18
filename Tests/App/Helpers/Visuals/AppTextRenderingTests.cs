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
    // The app used to override TextFormattingModeProperty's metadata on FrameworkElement and
    // FrameworkContentElement to Display, and repeated that on five roots by hand. Nothing may force it
    // back: at 11 DIP the two modes measure 24.187 against 24.000, under 1%, while the six places that
    // build a FormattedText to size a run all use the plain constructor, which measures Ideal. Forcing
    // Display therefore meant laying text out on metrics the app's own measuring code never asked for.
    [StaTestMethod]
    public void NoFormattingModeIsForcedOntoAnySurface()
    {
        // Touching App is what runs a static constructor, and it has to happen before any element is
        // created: an override added there again would only be caught by this test if this test made the
        // override exist.
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
                Assert.AreEqual(TextFormattingMode.Ideal, TextOptions.GetTextFormattingMode(element), element.GetType().FullName);
        }
        finally
        {
            pluginWindow.Close();
            window.Close();
        }
    }

    // Leaving the default alone must not mean losing the choice: a surface that wants Display still gets
    // it by asking, and it inherits down to the text and its runs.
    [StaTestMethod]
    public void FormattingModeCanBeStillOptedIntoOnASubtree()
    {
        var run = new Run("Explicit formatting");
        var text = new TextBlock(run);
        var parent = new Border { Child = text };
        TextOptions.SetTextFormattingMode(parent, TextFormattingMode.Display);

        Assert.AreEqual(TextFormattingMode.Display, TextOptions.GetTextFormattingMode(text));
        Assert.AreEqual(TextFormattingMode.Display, TextOptions.GetTextFormattingMode(run));
    }
}
