using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using Lertaro.App.Services;
using Lertaro.App.ViewModels.Settings;
using Lertaro.App.Views.Settings;
using Lertaro.Core;
using Lertaro.PluginSdk.Windows;

namespace Lertaro.App.Tests.Helpers.Visuals;

[TestClass]
[DoNotParallelize]
public sealed class AppTypographyTests
{
    [StaTestMethod]
    [DataRow("zh-CN")]
    [DataRow("zh-TW")]
    [DataRow("zh-HK")]
    [DataRow("zh")]
    public void ChineseUiUsesNormalInsteadOfTheSystemDefaultWeight(string culture)
    {
        var root = new Window();
        AppTypography.Initialize(root.Resources, culture);
        var run = new Run("默认正文");
        var body = new TextBlock(run);
        var heading = new TextBlock { Text = "标题", FontWeight = FontWeights.Bold };
        var panel = new StackPanel();
        panel.Children.Add(body);
        panel.Children.Add(heading);
        root.Content = panel;
        try
        {
            root.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            Assert.AreEqual(400, body.FontWeight.ToOpenTypeWeight());
            Assert.AreEqual(400, run.FontWeight.ToOpenTypeWeight());
            Assert.AreEqual(FontWeights.Bold, heading.FontWeight);

            AppTypography.UpdateLanguage(root.Resources, "en-US");
            Assert.AreEqual(SystemFonts.MessageFontWeight, body.FontWeight);
            AppTypography.UpdateLanguage(root.Resources, culture);
            Assert.AreEqual(FontWeights.Normal, body.FontWeight);
        }
        finally
        {
            root.Close();
        }
    }

    [StaTestMethod]
    public void WindowsPluginsAndPopupRootsInheritFontsAndLiveLanguageChanges()
    {
        foreach (var root in new FrameworkElement[] { new Window(), new PluginWindow("Fonts"), new Popup(), new ContextMenu(), new ToolTip() })
        {
            try
            {
                AppTypography.Initialize(root.Resources, "zh-CN");
                var run = new Run("中文 日本語 한국어 English");
                var text = new TextBlock(run);
                switch (root)
                {
                    case PluginWindow plugin: plugin.ContentHostControl.Content = text; break;
                    case ContentControl content: content.Content = text; break;
                    case Popup popup: popup.Child = text; break;
                    case ContextMenu menu: menu.Items.Add(text); break;
                }
                root.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));

                Assert.AreEqual(AppTypography.UiFont, text.FontFamily, root.GetType().Name);
                Assert.AreEqual(FontWeights.Normal, text.FontWeight, root.GetType().Name);
                Assert.AreEqual(FontWeights.Normal, run.FontWeight, root.GetType().Name);
                Assert.AreEqual("zh-cn", text.Language.IetfLanguageTag);
                AppTypography.UpdateLanguage(root.Resources, "ja-JP");
                Assert.AreEqual("ja-jp", text.Language.IetfLanguageTag);
                Assert.AreEqual("ja-jp", run.Language.IetfLanguageTag);

                var replacement = new FontFamily("Tahoma");
                root.Resources[AppTypography.UiFontKey] = replacement;
                Assert.AreEqual(replacement, text.FontFamily, "existing content must follow the central font resource");
            }
            finally
            {
                if (root is Window window) window.Close();
            }
        }
    }

    [StaTestMethod]
    public void StyledMenuHeadersInheritTheCentralChineseTypography()
    {
        var menu = new ContextMenu();
        menu.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/Lertaro.App;component/Resources/Styles/Controls/Menu.xaml", UriKind.Relative)
        });
        AppTypography.Initialize(menu.Resources, "zh-CN");
        var icon = new TextBlock { Text = "\uE946", FontFamily = AppTypography.IconFont, FontSize = 14 };
        var item = new MenuItem { Header = "关于", Icon = icon };
        menu.Items.Add(item);
        menu.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        menu.Measure(new Size(500, 500));
        menu.Arrange(new Rect(menu.DesiredSize));
        menu.UpdateLayout();

        static IEnumerable<TextBlock> TextBlocks(DependencyObject root)
        {
            if (root is TextBlock text) yield return text;
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
                foreach (var child in TextBlocks(VisualTreeHelper.GetChild(root, i)))
                    yield return child;
        }

        var header = Assert.ContainsSingle(TextBlocks(item).Where(text => text.Text == "关于"));
        Assert.AreEqual(AppTypography.UiFont, header.FontFamily);
        Assert.AreEqual(FontWeights.Normal, header.FontWeight);
        Assert.AreEqual("zh-cn", header.Language.IetfLanguageTag);
        Assert.AreEqual(12.5, header.FontSize);
        Assert.AreEqual(AppTypography.IconFont, icon.FontFamily);
        var arrow = Assert.ContainsSingle(TextBlocks(item).Where(text => text.Name == "Arrow"));
        Assert.AreEqual(AppTypography.IconFont, arrow.FontFamily);

        AppTypography.UpdateLanguage(menu.Resources, "zh-TW");
        Assert.AreEqual("zh-tw", header.Language.IetfLanguageTag);
        Assert.AreEqual(FontWeights.Normal, header.FontWeight);
    }

    [StaTestMethod]
    public void ExplicitContentLanguageAndIconFontsRemainIntact()
    {
        var icon = new TextBlock { FontFamily = new FontFamily("Segoe MDL2 Assets"), Text = "\uE721" };
        var root = new ToolTip { Content = icon, Language = XmlLanguage.GetLanguage("ko-KR") };
        AppTypography.Initialize(root.Resources, "zh-CN");
        root.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        AppTypography.UpdateLanguage(root.Resources, "ja-JP");

        Assert.AreEqual("Segoe MDL2 Assets", icon.FontFamily.Source);
        Assert.AreEqual("ko-kr", icon.Language.IetfLanguageTag);
    }

    [StaTestMethod]
    public void ExplicitRootWeightIsNotReplacedByTheChineseDefault()
    {
        var style = new Style(typeof(ToolTip));
        style.Setters.Add(new Setter(TextElement.FontWeightProperty, FontWeights.SemiBold));
        var text = new TextBlock { Text = "标题" };
        var root = new ToolTip { Style = style, Content = text };
        AppTypography.Initialize(root.Resources, "zh-CN");
        root.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));

        Assert.AreEqual(FontWeights.SemiBold, text.FontWeight);
    }

    [TestMethod]
    [DataRow("zh-CN", "zh-cn")]
    [DataRow("zh-TW", "zh-tw")]
    [DataRow("zh-HK", "zh-hk")]
    [DataRow("ja-JP", "ja-jp")]
    [DataRow("ko-KR", "ko-kr")]
    [DataRow("ar-SA", "ar-sa")]
    [DataRow("hi-IN", "hi-in")]
    [DataRow("", "en-us")]
    [DataRow(null, "en-us")]
    [DataRow("not a language", "en-us")]
    public void LanguageSelectionPreservesRegionalTagsAndHandlesInvalidSettings(string? culture, string expected) =>
        Assert.AreEqual(expected, AppTypography.ResolveLanguage(culture).IetfLanguageTag);

    [StaTestMethod]
    public void LogFontHasEqualLatinAdvancesAndSupportsMixedText()
    {
        var typeface = new Typeface(AppTypography.MonospaceFont, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        double Width(string value) => new FormattedText(value, CultureInfo.GetCultureInfo("zh-CN"),
            FlowDirection.LeftToRight, typeface, 11, Brushes.Black, null, TextFormattingMode.Display, 1).WidthIncludingTrailingWhitespace;

        Assert.AreEqual(Width("iiii"), Width("WWWW"), 0.01, "Latin log columns must use fixed-width advances");
        Assert.IsGreaterThan(Width("INFO "), Width("INFO 中文日志"));
    }

    [StaTestMethod]
    public void LogDocumentUsesTheSharedFontAndLanguageWithoutChangingCopiedText()
    {
        RuntimeHelpers.RunClassConstructor(typeof(Lertaro.App.App).TypeHandle);
        var root = new Window();
        AppTypography.Initialize(root.Resources, "zh-TW");
        var page = new ServiceSettingsPage();
        root.Content = page;
        root.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        const string message = "INFO 繁體中文 日本語 한국어 path=C:\\日志\\a.txt";
        try
        {
            page.RebuildLogDocument([new LogLineViewModel(message, LogLevel.Info)]);
            var document = page.LogTextBox.Document;
            Assert.AreEqual(AppTypography.MonospaceFont, document.FontFamily);
            Assert.AreEqual(FontWeights.Normal, document.FontWeight);
            Assert.AreEqual("zh-tw", document.Language.IetfLanguageTag);
            Assert.AreEqual(message, new TextRange(document.ContentStart, document.ContentEnd).Text.TrimEnd('\r', '\n'));

            AppTypography.UpdateLanguage(root.Resources, "ja-JP");
            Assert.AreEqual("ja-jp", document.Language.IetfLanguageTag, "an existing log document must follow language changes");
        }
        finally
        {
            root.Close();
        }
    }
}
