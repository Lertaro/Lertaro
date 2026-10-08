using System.Globalization;
using System.Xml.Linq;

namespace Lertaro.Core.Tests;

[TestClass]
public sealed class ThemeContrastTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static readonly string[] Surfaces =
    [
        "ContentBg", "CardBackground", "SettingsCardBackground", "ControlBackground",
        "ControlHoverBackground", "HoverBackground", "SidebarBg", "SidebarHover",
        "StatusBarBg", "HeaderColumnBg", "ActionsHeaderBg", "HeaderBg", "MenuBackground",
    ];
    private static readonly Rgba[] Desktops = [Parse("#000000"), Parse("#FFFFFF")];

    public static IEnumerable<string> Themes()
    {
        var root = RepositoryRoot();
        foreach (var path in Directory.EnumerateFiles(Path.Combine(root, "Plugins"), "*.xaml", SearchOption.AllDirectories)
                     .Where(p => p.Split(Path.DirectorySeparatorChar).Contains("Themes"))
                     .Where(p => !p.Split(Path.DirectorySeparatorChar).Any(s => s is "obj" or "bin"))
                     .Order())
            yield return path;
    }

    [TestMethod]
    public void ThemeInventory_IncludesEveryShippedPack()
    {
        var paths = Themes().ToArray();
        Assert.IsGreaterThanOrEqualTo(48, paths.Length);
        foreach (var pack in new[] { "CoreExtensions", "CuratedThemes", "AnimeThemes" })
            Assert.IsNotEmpty(paths.Where(p => p.Split(Path.DirectorySeparatorChar).Contains(pack)), pack);
    }

    [TestMethod]
    [DynamicData(nameof(Themes))]
    public void Theme_TextRemainsReadableOnActualSurfacesAndButtonStates(string path)
    {
        var resources = ReadResources(path);
        var pairs = new List<(string Foreground, string Background)>();
        foreach (var foreground in new[] { "TextPrimary", "TextPrimary2", "TextSecondary", "TextSecondary2", "AccentBlue", "AccentColor" })
            pairs.AddRange(Surfaces.Select(background => (foreground, background)));
        pairs.AddRange(new (string, string)[]
        {
            ("PrimaryButtonText", "AccentBlue"),
            ("PrimaryButtonText", "PrimaryButtonHoverBackground"),
            ("PrimaryButtonText", "PrimaryButtonPressedBackground"),
            ("SecondaryButtonText", "ControlBackground"),
            ("SecondaryButtonHoverText", "ControlHoverBackground"),
            ("BadgeText", "BadgeBackground"),
            ("SuccessBadgeText", "SuccessBadgeBackground"),
            ("SuccessBadgeText", "SettingsCardBackground"),
            ("WarningBrush", "WarningBackground"),
            ("WarningBrush", "SettingsCardBackground"),
            ("ErrorBrush", "ErrorBackground"),
            ("ErrorBrush", "SettingsCardBackground"),
            ("MenuText", "MenuBackground"),
            ("MenuText", "HoverBackground"),
            ("SidebarText", "SidebarBg"),
            ("SidebarText", "SidebarHover"),
            ("SidebarTextActive", "SidebarHover"),
            ("HeaderColumnText", "HeaderColumnBg"),
            ("ShortcutColor", "HoverBackground"),
        });

        AssertContrast(path, resources, pairs, 4.5);
    }

    [TestMethod]
    [DynamicData(nameof(Themes))]
    public void Theme_ControlsAndSelectionIndicatorsRemainVisible(string path)
    {
        var resources = ReadResources(path);
        var pairs = new List<(string Foreground, string Background)>();
        foreach (var foreground in new[]
                 {
                     "ScrollBarThumbBackground", "ScrollBarThumbHover", "ScrollBarThumbDragging",
                     "ControlBorderBrush", "AccentBarColor",
                 })
            pairs.AddRange(Surfaces.Select(background => (foreground, background)));
        pairs.Add(("PrimaryButtonText", "CloseButtonHoverBg"));
        AssertContrast(path, resources, pairs, 3.0);
    }

    [TestMethod]
    [DataRow("#000000", "#FFFFFF", 21.0)]
    [DataRow("#717171", "#717171", 1.0)]
    [DataRow("#777777", "#FFFFFF", 4.478)]
    [DataRow("#80000000", "#FFFFFF", 4.004)]
    [DataRow("#00FFFFFF", "#123456", 1.0)]
    public void Contrast_UsesLinearLuminanceAndCompositedAlpha(string foreground, string background, double expected)
    {
        var backdrop = Parse(background);
        Assert.AreEqual(expected, Contrast(Parse(foreground).Over(backdrop), backdrop), 0.002);
    }

    [TestMethod]
    public void GradientSampling_CatchesUnreadableInteriorEvenWhenEndpointsPass()
    {
        var brush = XElement.Parse("""
            <LinearGradientBrush>
                <GradientStop Color="#FF0000" Offset="0"/>
                <GradientStop Color="#00FF00" Offset="1"/>
            </LinearGradientBrush>
            """);
        var foreground = Parse("#000000");
        Assert.IsGreaterThan(4.5, Contrast(foreground, Parse("#FF0000")));
        Assert.IsGreaterThan(4.5, Contrast(foreground, Parse("#00FF00")));
        Assert.IsLessThan(4.5, Samples(brush).Min(background => Contrast(foreground, background)));
    }

    private static void AssertContrast(string path, Dictionary<string, XElement> resources,
        IEnumerable<(string Foreground, string Background)> pairs, double minimum)
    {
        var opacity = double.Parse(resources["WindowOpacity"].Value, CultureInfo.InvariantCulture);
        var baseColors = Samples(resources["ContentBg"]).ToArray();
        var samples = resources.Where(p => p.Value.Name.LocalName.EndsWith("Brush", StringComparison.Ordinal))
            .ToDictionary(p => p.Key, p => Samples(p.Value).ToArray());
        var failures = new List<string>();
        foreach (var (foregroundKey, backgroundKey) in pairs)
        {
            var worst = double.MaxValue;
            foreach (var foreground in samples[foregroundKey])
            foreach (var background in samples[backgroundKey])
            foreach (var desktop in Desktops)
            // A quick-search outer card can sit directly on the desktop; settings cards sit on ContentBg.
            foreach (var behind in baseColors.Append(desktop))
            {
                var backdrop = background.Over(behind.Over(desktop));
                var text = foreground.Over(backdrop);
                var displayedText = (text with { A = opacity }).Over(desktop);
                var displayedBackground = (backdrop with { A = opacity }).Over(desktop);
                worst = Math.Min(worst, Contrast(displayedText, displayedBackground));
            }
            if (worst < minimum)
                failures.Add($"{foregroundKey} on {backgroundKey}: {worst:F2}:1 (needs {minimum}:1)");
        }
        Assert.IsEmpty(failures, $"{Path.GetFileName(path)}{Environment.NewLine}{string.Join(Environment.NewLine, failures)}");
    }

    private static Dictionary<string, XElement> ReadResources(string path) =>
        XDocument.Load(path).Root!.Elements().Where(e => e.Attribute(Xaml + "Key") != null)
            .ToDictionary(e => (string)e.Attribute(Xaml + "Key")!, e => e);

    private static IEnumerable<Rgba> Samples(XElement brush)
    {
        if (brush.Name.LocalName == "SolidColorBrush")
        {
            var color = Parse((string)brush.Attribute("Color")!);
            yield return color with { A = color.A * ((double?)brush.Attribute("Opacity") ?? 1) };
            yield break;
        }

        // ponytail: sample the VisualBrush's base gradient; moving decorations still need a rendered
        // review. If themes gain image backgrounds, replace this bound with pixel sampling.
        var gradient = brush.Name.LocalName == "VisualBrush"
            ? brush.Descendants().Single(e => e.Name.LocalName == "Grid.Background").Elements().Single()
            : brush;
        var stops = gradient.Descendants().Where(e => e.Name.LocalName == "GradientStop")
            .Select(e => Parse((string)e.Attribute("Color")!)).ToArray();
        Assert.IsNotEmpty(stops, $"Unsupported or empty brush: {brush}");
        var opacity = (double?)gradient.Attribute("Opacity") ?? 1;
        if (stops.Length == 1)
            yield return stops[0] with { A = stops[0].A * opacity };
        for (var i = 1; i < stops.Length; i++)
        for (var step = 0; step <= 32; step++)
        {
            var sample = stops[i - 1].Mix(stops[i], step / 32.0);
            yield return sample with { A = sample.A * opacity };
        }
    }

    private static Rgba Parse(string hex)
    {
        var value = uint.Parse(hex.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return new Rgba(((value >> 16) & 255) / 255.0, ((value >> 8) & 255) / 255.0,
            (value & 255) / 255.0, hex.Length == 9 ? (value >> 24) / 255.0 : 1);
    }

    private static double Contrast(Rgba a, Rgba b) =>
        (Math.Max(a.Luminance, b.Luminance) + 0.05) / (Math.Min(a.Luminance, b.Luminance) + 0.05);

    private readonly record struct Rgba(double R, double G, double B, double A)
    {
        public Rgba Mix(Rgba other, double amount) =>
            new(R + (other.R - R) * amount, G + (other.G - G) * amount,
                B + (other.B - B) * amount, A + (other.A - A) * amount);

        // Every backdrop passed here is already opaque.
        public Rgba Over(Rgba background) => background.Mix(this, A) with { A = 1 };

        public double Luminance => 0.2126 * Linear(R) + 0.7152 * Linear(G) + 0.0722 * Linear(B);
        private static double Linear(double channel) =>
            channel <= 0.04045 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);
    }

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AGENTS.md")))
            dir = dir.Parent;
        Assert.IsNotNull(dir, "could not locate the repository root");
        return dir.FullName;
    }
}
