using System.Windows.Media;
using Lertaro.Plugins.CoreExtensions.Preview.Controls;

namespace Lertaro.Plugins.CoreExtensions.Tests.Preview.Controls;

// The transport bar's icons are plain Geometry mini-language strings assigned to a Path.Data at runtime
// (see MediaPreviewControl.UpdatePlayPauseIcon/UpdateMuteIcon) rather than compiled XAML markup, so a
// typo in one wouldn't be caught by the build -- only by Geometry.Parse throwing the first time a user
// actually opens a media file. Parsing them here catches that at test time instead.
[TestClass]
public sealed class MediaPreviewControlIconTests
{
    [TestMethod]
    public void PlayIconData_ParsesToNonEmptyGeometry() => AssertParsesToNonEmptyGeometry(MediaPreviewControl.PlayIconData);

    [TestMethod]
    public void PauseIconData_ParsesToNonEmptyGeometry() => AssertParsesToNonEmptyGeometry(MediaPreviewControl.PauseIconData);

    [TestMethod]
    public void VolumeIconData_ParsesToNonEmptyGeometry() => AssertParsesToNonEmptyGeometry(MediaPreviewControl.VolumeIconData);

    [TestMethod]
    public void MutedIconData_ParsesToNonEmptyGeometry() => AssertParsesToNonEmptyGeometry(MediaPreviewControl.MutedIconData);

    private static void AssertParsesToNonEmptyGeometry(string data)
    {
        var geometry = Geometry.Parse(data);
        Assert.IsFalse(geometry.IsEmpty());
        Assert.IsFalse(geometry.Bounds.IsEmpty);
    }
}
