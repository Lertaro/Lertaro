using System.Windows.Media;

namespace Lertaro.PluginSdk.Services;

/// <summary>
/// Provides access to the main application's cached shell icon service.
/// </summary>
public static class IconService
{
    /// <summary>
    /// Delegate registered by the main application to fetch file/directory icons.
    /// </summary>
    public static Func<string, bool, ImageSource?> GetIconFunc { get; set; } = (path, isDir) => null;

    /// <summary>
    /// Retrieves the cached icon for the specified path.
    /// </summary>
    public static ImageSource? GetIcon(string path, bool isDir) => GetIconFunc(path, isDir);

    /// <summary>
    /// Delegate registered by the main application for a cache-only, non-blocking icon lookup: returns
    /// whatever's already cached (or a generic placeholder) instantly, and reports whether the real icon
    /// still needs a slower fetch (e.g. GetIcon, off the UI thread) to resolve properly.
    /// </summary>
    public static Func<string, bool, (ImageSource? Icon, bool NeedsLoad)> GetIconCacheOnlyFunc { get; set; } = (path, isDir) => (null, false);

    /// <summary>
    /// Retrieves whatever icon is already cached for path (or a generic placeholder) without touching disk
    /// or the shell -- safe to call on the UI thread. needsLoad is true when the real icon hasn't been
    /// resolved yet and the caller should follow up with GetIcon (off the UI thread) to get it.
    /// </summary>
    public static ImageSource? GetIconFromCacheOnly(string path, bool isDir, out bool needsLoad)
    {
        var (icon, needs) = GetIconCacheOnlyFunc(path, isDir);
        needsLoad = needs;
        return icon;
    }

    /// <summary>
    /// Delegate registered by the main application to fetch a large real thumbnail (video frame, document
    /// page, image) for the given path at the requested pixel size, or null if unavailable.
    /// </summary>
    public static Func<string, int, ImageSource?> GetThumbnailFunc { get; set; } = (path, size) => null;

    /// <summary>
    /// Retrieves a large thumbnail for the path (uncached), or null when the shell has none.
    /// </summary>
    public static ImageSource? GetThumbnail(string path, int size) => GetThumbnailFunc(path, size);
}
