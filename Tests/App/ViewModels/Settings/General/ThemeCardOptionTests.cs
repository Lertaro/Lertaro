using System.Windows;
using System.Windows.Media;
using Lertaro.PluginSdk.Abstractions;
using Lertaro.App.ViewModels.Settings.General;

namespace Lertaro.App.Tests.ViewModels.Settings.General;

[TestClass]
public sealed class ThemeCardOptionTests
{
    private sealed class FakeTheme : ITheme
    {
        public string Id { get; init; } = "my-theme";
        public string DisplayName { get; init; } = "My Theme";
        public bool IsDark { get; init; }
        public ResourceDictionary Resources { get; } = new();
        public ResourceDictionary GetResources() => Resources;
        public double WindowOpacity => 1.0;
    }

    [TestMethod]
    public void Constructor_CopiesIdDisplayNameAndIsDarkFromTheme()
    {
        var theme = new FakeTheme { Id = "dark-1", DisplayName = "Midnight", IsDark = true };

        var option = new ThemeCardOption(theme);

        Assert.AreEqual("dark-1", option.Id);
        Assert.AreEqual("Midnight", option.DisplayName);
        Assert.IsTrue(option.IsDark);
    }

    [TestMethod]
    public void Constructor_KnownResourceKey_ResolvesToThatBrush()
    {
        var theme = new FakeTheme();
        theme.Resources["AccentColor"] = new SolidColorBrush(Colors.Red);

        var option = new ThemeCardOption(theme);

        Assert.AreEqual(Colors.Red, ((SolidColorBrush)option.Accent).Color);
    }

    [TestMethod]
    public void Constructor_MissingResourceKey_FallsBackToGray()
    {
        var option = new ThemeCardOption(new FakeTheme());

        Assert.AreEqual(Brushes.Gray, option.Accent);
    }

    [TestMethod]
    public void Constructor_ResourceKeyPresentButWrongType_FallsBackToGray()
    {
        var theme = new FakeTheme();
        theme.Resources["AccentColor"] = "not a brush";

        var option = new ThemeCardOption(theme);

        Assert.AreEqual(Brushes.Gray, option.Accent);
    }

    [TestMethod]
    public void Constructor_ResolvesEachDistinctResourceKeyIndependently()
    {
        var theme = new FakeTheme();
        theme.Resources["AccentColor"] = new SolidColorBrush(Colors.Blue);
        theme.Resources["CardBackground"] = new SolidColorBrush(Colors.White);

        var option = new ThemeCardOption(theme);

        Assert.AreEqual(Colors.Blue, ((SolidColorBrush)option.Accent).Color);
        Assert.AreEqual(Colors.White, ((SolidColorBrush)option.CardBg).Color);
        Assert.AreEqual(Brushes.Gray, option.CardBorder);
    }

    [TestMethod]
    public void Constructor_EveryResolvedBrushIsFrozen()
    {
        // Load-bearing, not hygiene: an UNFROZEN Freezable used as a dependency-property value acquires an
        // InheritanceContext back to the element that holds it (brush -> Rectangle -> ... -> the settings
        // window). These brushes stay reachable through WPF's binding tables well past the window's close,
        // so an unfrozen one kept each preview card's whole window alive -- one leaked window per open,
        // until a restart. A frozen Freezable has no such edge.
        var theme = new FakeTheme();
        foreach (var key in new[]
                 {
                     "AccentColor", "CardBackground", "CardBorderBrush", "ControlBackground",
                     "TextPrimary", "TextSecondary", "HoverBackground", "AccentBarColor", "PrimaryButtonText"
                 })
        {
            theme.Resources[key] = new SolidColorBrush(Colors.Red);
        }

        var option = new ThemeCardOption(theme);

        foreach (var (name, brush) in new (string, Brush)[]
                 {
                     ("Accent", option.Accent), ("CardBg", option.CardBg), ("CardBorder", option.CardBorder),
                     ("SearchBg", option.SearchBg), ("Text", option.Text), ("TextSecondary", option.TextSecondary),
                     ("SelectedRowBg", option.SelectedRowBg), ("AccentBar", option.AccentBar), ("AccentText", option.AccentText)
                 })
        {
            Assert.IsTrue(brush.IsFrozen, $"{name} must be frozen or it will pin the preview card's window");
        }
    }

    [TestMethod]
    public void Constructor_DoesNotFreezeTheThemesOwnBrush()
    {
        // The theme's dictionary is shared with the rest of the UI; freezing its brush in place would throw
        // the moment anything else tried to change it. The card gets a frozen COPY.
        var theme = new FakeTheme();
        var shared = new SolidColorBrush(Colors.Red);
        theme.Resources["AccentColor"] = shared;

        var option = new ThemeCardOption(theme);

        Assert.IsFalse(shared.IsFrozen, "the theme's own brush must be left mutable");
        Assert.AreNotSame(shared, option.Accent, "the card must hold its own copy, not the shared brush");
    }
}
