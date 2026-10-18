using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
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
        Assert.AreEqual(13, header.FontSize);
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

    // The rectangles that survived in the settings page were on plain CheckBox and Button instances: no
    // style of this app touches them, so nothing was ever going to null the property per style. Checked as
    // a local value rather than as the resolved property, because a theme style is not applied to a window
    // that was never measured -- an assertion on the resolved value would pass here even if the handler did
    // nothing at all.
    [StaTestMethod]
    public void PlainControlsAreGivenNoFocusRectangle()
    {
        var root = new Window();
        AppTypography.Initialize(root.Resources, "zh-CN");
        var check = new CheckBox { Content = "纳入此驱动器" };
        var button = new Button { Content = "重建索引" };
        var combo = new ComboBox();
        var panel = new StackPanel();
        panel.Children.Add(check);
        panel.Children.Add(button);
        panel.Children.Add(combo);
        root.Content = panel;

        foreach (var control in new System.Windows.Controls.Control[] { check, button, combo })
        {
            control.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            Assert.IsNull(control.ReadLocalValue(FrameworkElement.FocusVisualStyleProperty), control.GetType().Name);
        }
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
            FlowDirection.LeftToRight, typeface, 11, Brushes.Black, null, TextFormattingMode.Ideal, 1).WidthIncludingTrailingWhitespace;

        Assert.AreEqual(Width("iiii"), Width("WWWW"), 0.01, "Latin log columns must use fixed-width advances");
        Assert.IsGreaterThan(Width("INFO "), Width("INFO 中文日志"));
    }

    [StaTestMethod]
    public void LogDocumentUsesTheSharedFontAndLanguageWithoutChangingCopiedText()
    {
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

    // Section 四's font-size ladder. The cases that matter are the three ends of it: a computed size
    // between two rungs, a size below the first rung (a deliberate floor, so it is only made whole rather
    // than pushed up to 12, but made whole means rounded up -- the computed value is the smallest size the
    // text stays legible at, so rounding down would put it under that floor), and a size past the top rung
    // (nothing left to round up to, so only a whole number is asked for).
    [TestMethod]
    [DataRow(12.0, 12.0, DisplayName = "on a rung stays there")]
    [DataRow(12.8, 14.0, DisplayName = "just over the first rung rounds up")]
    [DataRow(13.2, 14.0, DisplayName = "between two rungs rounds up")]
    [DataRow(17.6, 18.0, DisplayName = "seventeen and a half is eighteen")]
    [DataRow(24.0, 24.0, DisplayName = "a mid rung is kept")]
    [DataRow(28.1, 32.0, DisplayName = "past the second to last rung")]
    [DataRow(36.0, 36.0, DisplayName = "the top rung is kept")]
    [DataRow(9.0, 9.0, DisplayName = "a whole size below the ladder is untouched")]
    [DataRow(9.4, 10.0, DisplayName = "below the ladder rounds up, never down onto the floor")]
    [DataRow(11.2, 12.0, DisplayName = "just under the first rung still reaches it")]
    [DataRow(11.5, 12.0, DisplayName = "a half point below the ladder is made whole")]
    [DataRow(37.4, 38.0, DisplayName = "past the top there is only a whole number, rounded up")]
    public void SnapFontSizePutsAComputedSizeOnTheLadder(double computed, double expected) =>
        Assert.AreEqual(expected, AppTypography.SnapFontSize(computed), 0.001);

    // The ladder is the only place a computed size becomes a rendered one, so it is also the only place a
    // fraction could still escape. Stepped through in tenths rather than trusted from the two Ceiling calls.
    [TestMethod]
    public void SnapFontSizeNeverReturnsAFraction()
    {
        var offenders = new List<double>();
        for (var tenth = 60; tenth <= 480; tenth++)
        {
            var size = tenth / 10.0;
            var snapped = AppTypography.SnapFontSize(size);
            if (snapped != Math.Round(snapped)) offenders.Add(size);
        }

        Assert.AreEqual(0, offenders.Count, $"non-whole results for: {string.Join(", ", offenders)}");
    }

    [TestMethod]
    public void SnapFontSizeLeavesAnUnsetSizeAlone() =>
        Assert.IsTrue(double.IsNaN(AppTypography.SnapFontSize(double.NaN)), "a NaN size (an unset FontSize) must stay NaN rather than become a number");

    // Section 四's whole-number rule, read back off the source instead of trusting the sweep that applied
    // it: the first sweep claimed 19 of these were gone, and four half-point sizes were still in the tree
    // plus one pinned by a test here -- all of them written as style setters, which is the spelling the
    // sweep's own search did not use. A size set literally never passes through SnapFontSize, so nothing
    // else keeps it off a half pixel. Scans both markup and code, because the size that survived longest
    // sat in a preview provider written in C#.
    [TestMethod]
    public void FontSizesInTheSourceAreWholeNumbers()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AGENTS.md"))) dir = dir.Parent;
        Assert.IsNotNull(dir, "could not locate the repository root");

        // Every spelling a literal size survives in: the attribute on an element, a style setter's Value,
        // the object initializer, and SetValue on the dependency property. A build-time markup extension
        // ({Binding}, {TemplateBinding}) is resolved at runtime by SnapFontSize, so it is the ladder's
        // business, not this one.
        string[] patterns =
        [
            @"FontSize=""([^""]+)""|Property=""FontSize""[^>]*?Value=""([^""]+)""",
            @"FontSize\s*=\s*([0-9][0-9.]*)",
            @"FontSizeProperty\s*,\s*([0-9][0-9.]*)"
        ];
        var offenders = new List<string>();
        foreach (var folder in new[] { "App", "PluginSdk", "Plugins", "Core" })
            foreach (var file in Directory.EnumerateFiles(Path.Combine(dir!.FullName, folder), "*.*", SearchOption.AllDirectories)
                         .Where(f => f.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                    || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")) continue;
                var text = File.ReadAllText(file);
                foreach (var pattern in patterns)
                    foreach (Match match in Regex.Matches(text, pattern))
                    {
                        var value = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
                        if (value.StartsWith('{') || !double.TryParse(value, CultureInfo.InvariantCulture, out var size) || size == Math.Round(size)) continue;
                        offenders.Add($"{Path.GetRelativePath(dir.FullName, file)} = {size}");
                    }
            }

        Assert.AreEqual(0, offenders.Count, $"fractional font sizes: {string.Join(", ", offenders)}");
    }
}
