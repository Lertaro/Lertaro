using System.IO;
using System.Windows.Media.Imaging;

namespace Lertaro.App.Helpers;

/// <summary>
/// Writes the clipboard's image out as a PNG, for the callers that work in paths rather than pixels: the
/// LocalSend panel can only send what is actually on disk, and a screenshot never is.
/// </summary>
/// <remarks>
/// Needs an STA thread, exactly like <see cref="ClipboardTextHelper">the text side of the clipboard</see>:
/// WPF's clipboard is COM-backed. Its one caller reaches it from the UI thread.
///
/// ponytail: covers the bitmap formats WPF itself recognises (CF_BITMAP, CF_DIB, CF_DIBV5). A source that
/// offers nothing but a bare PNG or JPEG payload reads as no image, and the upgrade path is to sniff those
/// formats off the data object in <see cref="TryWriteTempPng"/>, which is where every caller already lands.
///
/// ponytail: what is written stays under the temp folder until Windows or Disk Cleanup takes it. Deleting
/// it on the panel's close would be wrong rather than merely lazy: a send reads the file after that panel
/// is gone, and a failed send leaves the item behind to retry.
/// </remarks>
internal static class ClipboardImageHelper
{
    private static string TempFolder => Path.Combine(Path.GetTempPath(), "Lertaro", "Clipboard");

    /// <summary>
    /// Writes the clipboard's image to a new PNG and returns its path, or null when there was no image to
    /// write.
    /// </summary>
    /// <remarks>
    /// A clipboard another process owns right now is reported the same way as a clipboard holding text
    /// only: the caller can neither tell them apart nor do anything better about either than say so.
    /// </remarks>
    internal static string? TryWriteTempPng()
    {
        try
        {
            if (!System.Windows.Clipboard.ContainsImage()) return null;
            if (System.Windows.Clipboard.GetImage() is not { } image) return null;
            return WriteTempPng(image);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Encodes <paramref name="image"/> as a PNG inside <see cref="TempFolder"/> and returns its path.</summary>
    internal static string WriteTempPng(BitmapSource image)
    {
        Directory.CreateDirectory(TempFolder);

        var path = NextTempPath(TempFolder);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using (var stream = File.Create(path)) encoder.Save(stream);
        return path;
    }

    /// <summary>
    /// The first free <c>clipboard-&lt;stamp&gt;.png</c> in <paramref name="folder"/>. Two copies in one
    /// second are two files: writing the second under the first one's name would leave the item already in
    /// the list pointing at the new image's bytes.
    /// </summary>
    internal static string NextTempPath(string folder)
    {
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var path = Path.Combine(folder, $"clipboard-{stamp}.png");
        for (var n = 2; File.Exists(path); n++)
            path = Path.Combine(folder, $"clipboard-{stamp}-{n}.png");

        return path;
    }
}
