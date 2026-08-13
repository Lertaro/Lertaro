using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using Lertaro.App.Services.Plugin;
using Lertaro.Core;
namespace Lertaro.App.Services.ShellIcons;

public static class ShellIconHelper
{
    private static readonly ConcurrentDictionary<string, ImageSource> _iconCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, ImageSource> _hashToIcon = new();

    public static void ClearCache()
    {
        _iconCache.Clear();
        _hashToIcon.Clear();
    }

    private const int MaxIconCacheEntries = 2000;

    // Coarse upper bound so a long session that touches many distinct file icons can't grow the cache
    // without limit. Entries are frozen bitmaps that reload cheaply, so clearing wholesale is fine.
    private static void EnforceCacheLimit()
    {
        if (_iconCache.Count > MaxIconCacheEntries)
        {
            _iconCache.Clear();
            _hashToIcon.Clear();
        }
    }

    private static ImageSource CacheAndDeduplicateIcon(string cacheKey, ImageSource icon)
    {
        var hash = ShellIconImageHash.GetHashFromImage(icon);
        if (hash != null)
        {
            if (_hashToIcon.TryGetValue(hash, out var existingIcon))
            {
                _iconCache[cacheKey] = existingIcon;
                return existingIcon;
            }
            _hashToIcon[hash] = icon;
        }
        _iconCache[cacheKey] = icon;
        return icon;
    }

    public static ImageSource? GetIconFromCacheOnly(string path, bool isDir, out bool needsLoad)
    {
        needsLoad = false;
        EnforceCacheLimit();
        if (path == "__NO_RESULTS__") return null;
        if (path == "__SHOW_MORE__") return VectorIconFactory.ShowMore();

        // Shell icon/thumbnail resolution can wake an idle WSL distro. Indexed WSL results always use
        // the generic folder or extension icon; explicit open/locate/preview paths remain unaffected.
        if (WslPath.IsPath(path))
            return GetIconForPath(isDir ? "dummy_folder" : "dummy" + Path.GetExtension(path), isDir);

        var ext = isDir ? "::directory::" : Path.GetExtension(path);
        if (string.IsNullOrEmpty(ext))
        {
            ext = "::unknown::";
        }

        var isVirtualItem = path.StartsWith("::") || path.StartsWith("shell:");
        var isPhysicalPath = !isVirtualItem && (File.Exists(path) || Directory.Exists(path));
        var hasThumbnailProvider = !isDir && PluginManager.Instance.ThumbnailProviders.Any(p => p.CanProvideThumbnail(path, isDir));

        // All real physical files on disk, virtual items, and thumbnail provider targets use full path as cache key
        var isUniqueIconType = isPhysicalPath || isVirtualItem || hasThumbnailProvider;
        var cacheKey = isUniqueIconType ? path : ext;

        if (_iconCache.TryGetValue(cacheKey, out var cachedIcon))
        {
            return cachedIcon;
        }

        if (isUniqueIconType)
        {
            needsLoad = true;

            if (!isDir && hasThumbnailProvider)
            {
                // Return specific file type icon as placeholder instead of generic unknown icon
                var extPlaceholderKey = $"::placeholder:{ext}::";
                if (_iconCache.TryGetValue(extPlaceholderKey, out var extPlaceholder))
                {
                    return extPlaceholder;
                }
                var fetchedExtPlaceholder = GetIconForPath("dummy" + ext, false);
                if (fetchedExtPlaceholder != null)
                {
                    _iconCache[extPlaceholderKey] = fetchedExtPlaceholder;
                    return fetchedExtPlaceholder;
                }
            }

            // Return generic placeholder icon instantly
            var placeholderKey = isDir ? "::directory::" : "::unknown::";
            if (_iconCache.TryGetValue(placeholderKey, out var placeholder))
            {
                return placeholder;
            }

            // Fetch placeholder icon synchronously via USEFILEATTRIBUTES fast path
            var fetchedPlaceholder = GetIconForPath(isDir ? "dummy_folder" : "dummy_unknown", isDir);
            if (fetchedPlaceholder != null)
            {
                _iconCache[placeholderKey] = fetchedPlaceholder;
            }
            return fetchedPlaceholder;
        }
        else
        {
            // Non-physical/fallback types can be resolved synchronously
            return GetIconForPath(path, isDir);
        }
    }

    public static ImageSource? GetIconForPath(this string path, bool isDir)
    {
        EnforceCacheLimit();
        if (path == "__NO_RESULTS__")
            return null;

        if (path == "__SHOW_MORE__")
        {
            return VectorIconFactory.ShowMore();
        }

        if (WslPath.IsPath(path))
            return GetIconForPath(isDir ? "dummy_folder" : "dummy" + Path.GetExtension(path), isDir);

        var ext = isDir ? "::directory::" : Path.GetExtension(path);
        if (string.IsNullOrEmpty(ext))
        {
            ext = "::unknown::";
        }

        var checkPath = path;
        var isVirtualItem = checkPath.StartsWith("::") || checkPath.StartsWith("shell:");
        var isPhysicalPath = !isVirtualItem && (File.Exists(checkPath) || Directory.Exists(checkPath));
        var hasThumbnailProvider = !isDir && PluginManager.Instance.ThumbnailProviders.Any(p => p.CanProvideThumbnail(path, isDir));

        var isUniqueIconType = isPhysicalPath || isVirtualItem || hasThumbnailProvider;
        var cacheKey = isUniqueIconType ? path : ext;

        if (_iconCache.TryGetValue(cacheKey, out var cachedIcon))
        {
            return cachedIcon;
        }

        // Check custom plugin thumbnail providers first
        var thumbnailProvider = PluginManager.Instance.ThumbnailProviders.FirstOrDefault(p => p.CanProvideThumbnail(path, isDir));
        if (thumbnailProvider != null)
        {
            try
            {
                var thumb = thumbnailProvider.GetThumbnail(path, ShellImageListInterop.PreferredPixels());
                if (thumb != null)
                {
                    return CacheAndDeduplicateIcon(cacheKey, thumb);
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"[ShellIconHelper] Thumbnail provider '{thumbnailProvider.Name}' failed: {ex.Message}", LogLevel.Error);
            }
        }

        try
        {
            var shfi = new ShellIconNativeMethods.SHFILEINFOW();

            if (!isDir && ext.Equals(".lnk", StringComparison.OrdinalIgnoreCase) && File.Exists(checkPath))
            {
                var shortcutIcon = ShellIconShortcutResolver.TryGetShortcutTargetIcon(checkPath);
                if (shortcutIcon != null)
                {
                    return CacheAndDeduplicateIcon(cacheKey, shortcutIcon);
                }
            }

            if (!isDir && ext.Equals(".msc", StringComparison.OrdinalIgnoreCase) && File.Exists(checkPath))
            {
                var mscIcon = ShellIconShortcutResolver.TryGetMscIcon(checkPath);
                if (mscIcon != null)
                {
                    return CacheAndDeduplicateIcon(cacheKey, mscIcon);
                }
            }

            if (isVirtualItem || (isDir && Directory.Exists(checkPath)))
            {
                // Safely load system shell icon by path instead of dangerous PIDL extraction
                var hiRes = ShellImageListInterop.TryGetIcon(checkPath, 0, 0);
                if (hiRes != null)
                {
                    return CacheAndDeduplicateIcon(cacheKey, hiRes);
                }
            }

            if (!isDir && (ext.Equals(".exe", StringComparison.OrdinalIgnoreCase) || ext.Equals(".ico", StringComparison.OrdinalIgnoreCase)) && File.Exists(checkPath))
            {
                var exeIcon = ShellImageListInterop.ExtractHiRes(checkPath, 0);
                if (exeIcon != null)
                {
                    return CacheAndDeduplicateIcon(cacheKey, exeIcon);
                }
            }

            if (isPhysicalPath)
            {
                // For real files/folders existing on disk, load the actual icon via real path
                // to avoid crashy third-party Shell extensions (Issue #222)
                var hiRes = ShellImageListInterop.TryGetIcon(checkPath, 0, 0);
                if (hiRes != null)
                {
                    return CacheAndDeduplicateIcon(cacheKey, hiRes);
                }

                // Fallback using USEFILEATTRIBUTES if real-path fetch returned null
                var fallbackPath = isDir ? "dummy_folder" : ext;
                var fallbackAttr = isDir ? ShellIconNativeMethods.FILE_ATTRIBUTE_DIRECTORY : ShellIconNativeMethods.FILE_ATTRIBUTE_NORMAL;
                var fallbackHiRes = ShellImageListInterop.TryGetIcon(fallbackPath, fallbackAttr, ShellIconNativeMethods.SHGFI_USEFILEATTRIBUTES);
                if (fallbackHiRes != null)
                {
                    return CacheAndDeduplicateIcon(cacheKey, fallbackHiRes);
                }
            }
            else
            {
                // Generic fallback for non-existent paths or virtual items
                var flags = ShellIconNativeMethods.SHGFI_ICON | ShellIconNativeMethods.SHGFI_LARGEICON | ShellIconNativeMethods.SHGFI_USEFILEATTRIBUTES;
                var attributes = isDir ? ShellIconNativeMethods.FILE_ATTRIBUTE_DIRECTORY : ShellIconNativeMethods.FILE_ATTRIBUTE_NORMAL;
                var lookupPath = isDir ? "dummy_folder" : ext;

                var hiRes = ShellImageListInterop.TryGetIcon(lookupPath, attributes, ShellIconNativeMethods.SHGFI_USEFILEATTRIBUTES);
                if (hiRes != null)
                {
                    return CacheAndDeduplicateIcon(cacheKey, hiRes);
                }

                var res = ShellIconNativeMethods.SHGetFileInfoW(lookupPath, attributes, ref shfi, (uint)Marshal.SizeOf(shfi), flags);
                if (res != IntPtr.Zero && shfi.hIcon != IntPtr.Zero)
                {
                    try
                    {
                        var bitmapSource = Imaging.CreateBitmapSourceFromHIcon(
                            shfi.hIcon,
                            Int32Rect.Empty,
                            BitmapSizeOptions.FromEmptyOptions());
                        bitmapSource.Freeze();
                        return CacheAndDeduplicateIcon(cacheKey, bitmapSource);
                    }
                    finally
                    {
                        ShellIconNativeMethods.DestroyIcon(shfi.hIcon);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"[ShellIconHelper] Failed to get shell icon for {path}: {ex.Message}", LogLevel.Warn);
        }

        return null;
    }

    public static ImageSource CreateVectorIcon(string pathData, string colorHexOrKey) => VectorIconFactory.Create(pathData, colorHexOrKey);
}
