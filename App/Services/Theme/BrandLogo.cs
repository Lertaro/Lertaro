using System.Windows;
using System.Windows.Media;
using Application = System.Windows.Application;
using Color = System.Windows.Media.Color;

namespace Lertaro.App.Services.Theme;

/// <summary>
/// The brand mark as vector geometry, painted in the theme's accent.
/// </summary>
/// <remarks>
/// <see cref="Create(Color)"/> is a pure function returning a frozen image, so nothing here is
/// thread-affine and no mark can outlive the theme it was painted for.
/// </remarks>
public static class BrandLogo
{
    // tray.svg's viewBox is 0 0 1024 1024; the exporter's two nested matrix() groups multiply out
    // to identity, so the path data below is verbatim. WPF's path mini-language defaults to the
    // even-odd fill rule, which is what the source declares and what punches the lens's hole.
    const double SourceUnits = 1024;
    const double RenderUnits = 32;

    static readonly Geometry BarTop = Parsed("M324.602,0L965.94,0C997.984,0 1024,26.016 1024,58.06"
        + "C1024,90.104 997.984,116.119 965.94,116.119L573.331,116.119"
        + "C513.767,45.148 424.415,0 324.602,0Z");
    static readonly Geometry BarMiddle = Parsed("M619.553,188.97L847.94,188.97C879.984,188.97 906,214.986 906,247.03"
        + "C906,279.074 879.984,305.089 847.94,305.089L648.626,305.089"
        + "C646.176,263.845 636.01,224.664 619.553,188.97Z");
    static readonly Geometry BarBottom = Parsed("M644.835,377.94L729.698,377.94C761.742,377.94 787.758,403.956 787.758,436"
        + "C787.758,468.044 761.742,494.06 729.698,494.06L601.454,494.06"
        + "C622.877,459.157 637.886,419.902 644.835,377.94Z");
    static readonly Geometry Lens = Parsed("M407.99,638.375C381.376,645.44 353.426,649.205 324.602,649.205"
        + "C145.449,649.205 -0,503.755 -0,324.602C-0,145.449 145.449,0 324.602,0"
        + "C503.755,0 649.205,145.449 649.205,324.602C649.205,419.843 608.098,505.558 542.675,564.959"
        + "L783.24,903.716C807.585,937.997 799.517,985.594 765.236,1009.94"
        + "C730.955,1034.28 683.358,1026.22 659.013,991.935L409.46,640.521"
        + "C408.956,639.812 408.466,639.096 407.99,638.375Z"
        + "M324.602,126.595C215.319,126.595 126.595,215.319 126.595,324.602"
        + "C126.595,433.886 215.319,522.61 324.602,522.61C433.886,522.61 522.61,433.886 522.61,324.602"
        + "C522.61,215.319 433.886,126.595 324.602,126.595Z");
    static readonly Geometry Gloss = Parsed("M199.038,316.9C199.03,316.96 199.021,317.019 199.013,317.079"
        + "C197.258,328.848 186.278,336.979 174.509,335.224C162.739,333.47 154.609,322.49 156.364,310.721"
        + "C156.481,309.93 156.641,309.156 156.839,308.401C167.301,241.148 216.468,186.274 282.364,168.617"
        + "L282.366,168.627C293.794,165.564 305.586,172.304 308.733,183.728C311.893,195.2 305.144,207.079 293.672,210.239"
        + "C292.99,210.427 292.307,210.58 291.624,210.698C242.536,224.643 206.197,266.251 199.078,316.906L199.038,316.9Z");

    // Draw order matters: the source paints the lens over the bars where they overlap, so the five
    // stay separate drawings rather than merging into one GeometryGroup -- merging would let the
    // even-odd rule punch a hole wherever a bar crosses the lens.
    static readonly Geometry[] Layers = [BarTop, BarMiddle, BarBottom, Lens, Gloss];

    static readonly Color FallbackAccent = Color.FromRgb(0x05, 0x77, 0xbc);

    /// <summary>The mark in the current theme's accent, at 32x32 DIP.</summary>
    public static ImageSource Create() => Create(CurrentAccent());

    /// <summary>The mark at 32x32 DIP in one flat color.</summary>
    public static ImageSource Create(Color accent)
    {
        var brush = new SolidColorBrush(accent);
        brush.Freeze();
        var group = new DrawingGroup();
        foreach (var layer in Layers) group.Children.Add(new GeometryDrawing(brush, null, layer));
        var image = new DrawingImage(group);
        image.Freeze();
        return image;
    }

    static Color CurrentAccent() =>
        (Application.Current?.TryFindResource("AccentBlue") as SolidColorBrush)?.Color ?? FallbackAccent;

    static Geometry Parsed(string data)
    {
        // Geometry.Parse hands back a read-only instance, so Clone() is what makes it mutable
        // enough to carry the viewBox-to-DIP scale.
        var geometry = Geometry.Parse(data).Clone();
        geometry.Transform = new ScaleTransform(RenderUnits / SourceUnits, RenderUnits / SourceUnits);
        geometry.Freeze();
        return geometry;
    }
}
