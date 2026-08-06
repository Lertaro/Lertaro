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
        var brush = new SolidColorBrush(Colors.Red);
        theme.Resources["AccentColor"] = brush;

        var option = new ThemeCardOption(theme);

        Assert.AreSame(brush, option.Accent);
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
        var accent = new SolidColorBrush(Colors.Blue);
        var cardBg = new SolidColorBrush(Colors.White);
        theme.Resources["AccentColor"] = accent;
        theme.Resources["CardBackground"] = cardBg;

        var option = new ThemeCardOption(theme);

        Assert.AreSame(accent, option.Accent);
        Assert.AreSame(cardBg, option.CardBg);
        Assert.AreEqual(Brushes.Gray, option.CardBorder);
    }
}
