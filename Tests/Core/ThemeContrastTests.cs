using System.Globalization;
using System.Text.RegularExpressions;

namespace Lertaro.Core.Tests;

// Every theme's scrollbar thumb has to stay legible on every surface a scrollbar can land on.
//
// It exists because a theme cannot be checked by looking at it. A scrollbar sits on the window, on
// cards, and on the ControlBackground of a text box, and in a dark theme that last one can be thirty
// levels lighter than the window. A thumb colour picked against the window -- the obvious thing to do,
// and what the built-in Dark theme did, flattening VS Code's translucent slider over its own
// background -- lands within a few levels of ControlBackground and disappears, but ONLY inside
// multi-line text boxes. Nothing else in the theme looks wrong. Sixteen of the shipped themes were in
// that state at once and none of it was noticed until a user reported that text box scrollbars had
// stopped appearing.
//
// This cannot cover themes from third-party plugins, which reach the app through IThemeProvider and
// are never seen here. For those the contract is documented in ScrollBar.xaml and nothing more.
[TestClass]
public sealed class ThemeContrastTests
{
    // The surfaces a scrollbar can be painted over.
    private static readonly string[] BackgroundKeys =
    {
        "ContentBg", "CardBackground", "SettingsCardBackground",
        "MenuBackground", "SidebarBg", "ControlBackground",
    };

    private static readonly string[] ThumbKeys =
    {
        "ScrollBarThumbBackground", "ScrollBarThumbHover", "ScrollBarThumbDragging",
    };

    /// <summary>The thumb's resting opacity in ScrollBar.xaml, which is what a colour is judged at.</summary>
    private const double RestingOpacity = 0.6;

    /// <summary>
    /// Minimum perceived-brightness gap between the composited thumb and what is behind it. Set just
    /// below where the light themes nobody has complained about already sit, so it demands what is
    /// already known to read rather than a number picked out of the air.
    /// </summary>
    private const double MinimumContrast = 20.0;

    [TestMethod]
    public void EveryThemesScrollbarThumbIsVisibleOnEverySurfaceItCanLandOn()
    {
        var themes = ThemeFiles().ToArray();
        Assert.IsGreaterThan(20, themes.Length, "expected the shipped themes to be found");

        var failures = new List<string>();
        foreach (var path in themes)
        {
            var text = File.ReadAllText(path);
            var backgrounds = BackgroundKeys.SelectMany(key => ColoursOf(text, key)).ToArray();
            if (backgrounds.Length == 0) continue;

            foreach (var key in ThumbKeys)
            {
                var thumb = ColoursOf(text, key);
                if (thumb.Length == 0) continue;

                // Averaged: a gradient across a 4px-wide thumb reads as roughly its mean colour.
                var mean = Average(thumb);
                var worst = backgrounds.Min(background => Contrast(mean, background));
                if (worst < MinimumContrast)
                {
                    failures.Add($"{Path.GetFileNameWithoutExtension(path)}.{key} contrasts only " +
                                 $"{worst:F1} against its worst surface (needs {MinimumContrast})");
                }
            }
        }

        Assert.IsEmpty(failures, string.Join(Environment.NewLine, failures));
    }

    // Perceived brightness. Averaging the channels equally would pass a blue-heavy thumb the eye can
    // barely separate from its background.
    private static double Contrast((double R, double G, double B) thumb, (double R, double G, double B) background)
        => Math.Abs(0.2126 * RestingOpacity * (thumb.R - background.R) +
                    0.7152 * RestingOpacity * (thumb.G - background.G) +
                    0.0722 * RestingOpacity * (thumb.B - background.B));

    private static (double R, double G, double B) Average(IReadOnlyCollection<(double R, double G, double B)> colours)
        => (colours.Average(c => c.R), colours.Average(c => c.G), colours.Average(c => c.B));

    /// <summary>Every colour a key resolves to: one for a solid brush, one per stop for a gradient.</summary>
    private static (double R, double G, double B)[] ColoursOf(string text, string key)
    {
        var solid = Regex.Match(text, $"<SolidColorBrush x:Key=\"{key}\" Color=\"(#[0-9A-Fa-f]{{6,8}})\"");
        if (solid.Success) return [Parse(solid.Groups[1].Value)];

        var gradient = Regex.Match(text, $"<LinearGradientBrush x:Key=\"{key}\".*?</LinearGradientBrush>", RegexOptions.Singleline);
        if (!gradient.Success) return [];

        return Regex.Matches(gradient.Value, "Color=\"(#[0-9A-Fa-f]{6,8})\"")
            .Select(m => Parse(m.Groups[1].Value))
            .ToArray();
    }

    private static (double R, double G, double B) Parse(string hex)
    {
        var body = hex.TrimStart('#');
        if (body.Length == 8) body = body[2..]; // #AARRGGBB
        return (Channel(body, 0), Channel(body, 2), Channel(body, 4));
    }

    private static double Channel(string body, int index)
        => int.Parse(body.Substring(index, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);

    private static IEnumerable<string> ThemeFiles()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "AGENTS.md")))
            dir = dir.Parent;
        Assert.IsNotNull(dir, "could not locate the repository root");

        return Directory.EnumerateFiles(Path.Combine(dir!.FullName, "Plugins"), "*.xaml", SearchOption.AllDirectories)
            .Where(p => p.Split(Path.DirectorySeparatorChar).Contains("Themes"))
            .Where(p => !p.Split(Path.DirectorySeparatorChar).Any(s => s is "obj" or "bin"));
    }
}
