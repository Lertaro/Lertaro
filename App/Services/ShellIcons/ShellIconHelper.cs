using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using Lertaro.App.Services.Plugin;
using Lertaro.Core;
using Lertaro.PluginSdk.Abstractions.Plugins.Preview;
using Lertaro.PluginSdk.Helpers;
namespace Lertaro.App.Services.ShellIcons;

public static class ShellIconHelper
{
    private static readonly ConcurrentDictionary<string, ImageSource> _iconCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, ImageSource> _hashToIcon = new();

    /// <summary>
    /// Releases the per-file icons, keeping the extension icons a summon needs.
    /// </summary>
    /// <remarks>
    /// A search over a million files only ever shows a handful of distinct extension icons -- that is the
    /// whole reason an ordinary row draws its extension icon rather than a per-file one. Dropping those on
    /// hide is what made a second summon blank while the first had icons: nothing was cached left to draw.
    /// What a hide actually needs back is the memory the per-file icons took, so those go and the extension
    /// icons stay. This is also why the cost does not scale with the file count: a million files still means
    /// a few dozen extension entries.
    /// </remarks>
    public static void ClearCache()
    {
        try
        {
            foreach (var key in _iconCache.Keys)
            {
                // Extension keys are the bare ".pdf" / "::directory::" forms; a per-file key carries a
                // separator because it is a path. Keeping the extension ones is the whole point.
                if (key.IndexOf('\\') < 0 && key.IndexOf('/') < 0)
                    continue;

                _iconCache.TryRemove(key, out _);
            }

            _hashToIcon.Clear();
        }
        catch
        {
        }
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

    // Extensions whose icon is worth reading off the file itself. .exe and .lnk are deliberately NOT here:
    // reading theirs means touching the file (or resolving a shortcut through IShellLink), measured at a
    // steady 200-400 ms, longer than the list takes to paint -- so the row drew the type icon and swapped it
    // later, which reads as "the icons load twice". They now share the shell's own .exe / .lnk type icon, the
    // way a file manager draws a plain listing, and the cost stops scaling with how many executables a search
    // happens to match.
    private static bool IsUniqueIconExtension(string ext) =>
        ext.Equals(".exe", StringComparison.OrdinalIgnoreCase) ||
        ext.Equals(".lnk", StringComparison.OrdinalIgnoreCase) ||
        ext.Equals(".ico", StringComparison.OrdinalIgnoreCase) ||
        ext.Equals(".msc", StringComparison.OrdinalIgnoreCase) ||
        ext.Equals(".cpl", StringComparison.OrdinalIgnoreCase) ||
        ext.Equals(".url", StringComparison.OrdinalIgnoreCase);

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

        var isVirtualItem = UserPathResolver.IsVirtualPath(path);
        // Asked only when it can still change the answer. This is the binding-path getter for every
        // visible result row on every refresh, and CanProvideThumbnail is arbitrary third-party code
        // (wrapped in a Stopwatch and an allocation probe each) -- but a directory, a virtual token or an
        // extension already known to have its own icon is on the unique-icon path whatever the providers
        // say, and the placeholder branch below keys off the same extension in that case.
        // Bounded because this particular question is asked on the UI thread: one provider that never
        // answers used to be able to stop the window from painting, and now costs one budget and then
        // nothing at all until the cooldown lifts.
        var hasThumbnailProvider = !isDir && !isVirtualItem && !IsUniqueIconExtension(ext)
            && ThumbnailCallGuard.Run(() => PluginManager.Instance.ThumbnailProviders.Any(p => PluginPerformanceMonitor.Measure(p, () => p.CanProvideThumbnail(path, isDir))), path);
        // No isDir: a directory keyed itself by path while GetIconForPath keyed it by its "::directory::"
        // extension, so the folder icon was resolved and stored under a key the read side never asked
        // for either. Every folder draws the same icon, so the shared key is also the cheaper one.
        var isUniqueIconType = isVirtualItem || hasThumbnailProvider || IsUniqueIconExtension(ext);
        var cacheKey = isUniqueIconType ? path : ext;


        if (_iconCache.TryGetValue(cacheKey, out var cachedIcon))
        {
            return cachedIcon;
        }

        if (isUniqueIconType)
        {
            needsLoad = true;

            // A shortcut is the one unique-icon case whose type icon can be asked for without the file
            // existing, and the generic unknown placeholder used to be what a .lnk showed until its
            // shell target resolved -- a blank square that read as "still loading". Asking the shell for
            // the .lnk TYPE icon is a registration lookup (no file touched, cached after the first call),
            // so the row shows a real shortcut icon immediately and the accurate target icon lands later.
            if (!isDir && ext.Equals(".lnk", StringComparison.OrdinalIgnoreCase))
            {
                var shortcutKey = "::placeholder:.lnk::";
                if (_iconCache.TryGetValue(shortcutKey, out var shortcutPlaceholder))
                    return shortcutPlaceholder;

                var shortcutIcon = ShellImageListInterop.TryGetIcon(
                    "shortcut.lnk",
                    (uint)FileAttributes.Normal,
                    ShellIconNativeMethods.SHGFI_USEFILEATTRIBUTES);
                if (shortcutIcon != null)
                {
                    _iconCache[shortcutKey] = shortcutIcon;
                    needsLoad = true;
                    return shortcutIcon;
                }
            }

            if (!isDir && (hasThumbnailProvider || IsUniqueIconExtension(ext)))
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

        // Standard document types share one static extension icon resolved via USEFILEATTRIBUTES.
        // Directories reach here as well, and their "extension" is the ::directory:: token, so the lookup name
        // has to be the folder sentinel rather than "dummy" + token -- that concatenation produced a name the
        // shell could not resolve and folders lost their icon entirely.
        var sharedExtIcon = GetIconForPath(isDir ? "dummy_folder" : "dummy" + ext, isDir);
        if (sharedExtIcon != null)
        {
            _iconCache[ext] = sharedExtIcon;
        }
        return sharedExtIcon;
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
        var isVirtualItem = UserPathResolver.IsVirtualPath(checkPath);
        var isDummyPath = checkPath.StartsWith("dummy", StringComparison.OrdinalIgnoreCase);
        var isUnreachableNetwork = checkPath.StartsWith(@"\\", StringComparison.OrdinalIgnoreCase) && !ViewModels.Search.SearchReachabilityGate.IsPathReachable(checkPath);
        var isPhysicalPath = !isVirtualItem && !isDummyPath && !isUnreachableNetwork && (isDir ? Directory.Exists(checkPath) : File.Exists(checkPath));
        // One probe, used for both the cache-key decision and the thumbnail below. This asked twice --
        // an .Any here and a .FirstOrDefault after the cache check -- which ran every registered
        // third-party CanProvideThumbnail twice per uncached row. Bounded by the same call guard as the
        // binding-path question, since a provider that hangs here hangs the icon loader with it.
        var thumbnailProvider = !isDir
            ? ThumbnailCallGuard.Run(() => PluginManager.Instance.ThumbnailProviders.FirstOrDefault(p => PluginPerformanceMonitor.Measure(p, () => p.CanProvideThumbnail(path, isDir))), path)
            : null;

        // The key must match what GetIconFromCacheOnly looks up, or the icon this returns is stored
        // under a key nothing ever reads. That was the case: this side keyed every EXISTING file by its
        // path (isPhysicalPath) while the read side keyed anything without a unique-icon extension by
        // its extension, so an ordinary row never found the icon it had just resolved and went through
        // the placeholder path on every single search. Same three conditions as the read side now.
        var isUniqueIconType = isVirtualItem || thumbnailProvider != null || IsUniqueIconExtension(ext);
        var cacheKey = isUniqueIconType ? path : ext;

        if (_iconCache.TryGetValue(cacheKey, out var cachedIcon))
        {
            return cachedIcon;
        }

        // Every step answers null on its own failure and the chain moves to the next one. One try/catch used
        // to wrap the whole chain, so a single throwing step answered null for the row and cancelled the
        // fallbacks waiting after it -- which is how a row ended up stuck on its placeholder.
        //
        // A .lnk deliberately does NOT get its target resolved here. Resolving one costs an IShellLink COM
        // call plus a disk read, which is far more than the shell's own icon lookup and was the slowest thing
        // in a result list that contained a shortcut; the shell already answers SHGetFileInfo for a .lnk with
        // the shortcut icon (arrow included), which is what a file manager shows and what the row draws fast.
        var icon = Step(() => TryGetProviderThumbnail(thumbnailProvider, path), "thumbnail provider", path)
                   ?? Step(() => TryGetConsoleIcon(checkPath, ext, isDir, isPhysicalPath), "console file", path)
                   ?? Step(() => TryGetShellIconByPath(checkPath, isVirtualItem, isDir, isPhysicalPath), "shell icon by path", path)
                   ?? Step(() => TryGetEmbeddedIcon(checkPath, ext, isDir, isPhysicalPath), "embedded icon", path)
                   ?? (isPhysicalPath
                       ? Step(() => TryGetPhysicalIcon(checkPath, isDir, ext), "icon on disk", path)
                       : Step(() => TryGetGenericIcon(isDir, ext), "icon by extension", path));

        return icon == null ? null : CacheAndDeduplicateIcon(cacheKey, icon);
    }

    // One extraction step, contained: a throw is logged against the step that made it and answers null, so
    // the steps after it still get their turn.
    internal static ImageSource? Step(Func<ImageSource?> extract, string name, string path)
    {
        try
        {
            return extract();
        }
        catch (Exception ex)
        {
            Logger.Log($"[ShellIconHelper] {name} step failed for {path}: {ex.Message}", LogLevel.Warn);
            return null;
        }
    }

    private static ImageSource? TryGetProviderThumbnail(IThumbnailProvider? provider, string path)
    {
        if (provider == null)
            return null;

        // ThumbnailCallGuard bounds the wait; this catch only keeps the provider's own name in the log, which
        // the shared step message would have lost.
        try
        {
            return ThumbnailCallGuard.Run(() => PluginPerformanceMonitor.Measure(provider, () => provider.GetThumbnail(path, ShellImageListInterop.PreferredPixels())), path);
        }
        catch (Exception ex)
        {
            Logger.Log($"[ShellIconHelper] Thumbnail provider '{provider.Name}' failed: {ex.Message}", LogLevel.Error);
            return null;
        }
    }


    private static ImageSource? TryGetConsoleIcon(string path, string ext, bool isDir, bool isPhysicalPath) =>
        !isDir && ext.Equals(".msc", StringComparison.OrdinalIgnoreCase) && isPhysicalPath
            ? ShellIconShortcutResolver.TryGetMscIcon(path)
            : null;

    // Safely load system shell icon by path instead of dangerous PIDL extraction
    private static ImageSource? TryGetShellIconByPath(string path, bool isVirtualItem, bool isDir, bool isPhysicalPath) =>
        isVirtualItem || (isDir && isPhysicalPath)
            ? ShellImageListInterop.TryGetIcon(path, 0, 0)
            : null;

    private static ImageSource? TryGetEmbeddedIcon(string path, string ext, bool isDir, bool isPhysicalPath) =>
        !isDir && (ext.Equals(".exe", StringComparison.OrdinalIgnoreCase) || ext.Equals(".ico", StringComparison.OrdinalIgnoreCase)) && isPhysicalPath
            ? ShellImageListInterop.ExtractHiRes(path, 0)
            : null;

    // For real files/folders existing on disk, load the actual icon via the real path to avoid crashy
    // third-party Shell extensions (Issue #222), then the USEFILEATTRIBUTES fast path if that answered nothing.
    private static ImageSource? TryGetPhysicalIcon(string path, bool isDir, string ext)
    {
        var hiRes = ShellImageListInterop.TryGetIcon(path, 0, 0);
        if (hiRes != null)
            return hiRes;

        return ShellImageListInterop.TryGetIcon(isDir ? "dummy_folder" : ext,
                                                isDir ? ShellIconNativeMethods.FILE_ATTRIBUTE_DIRECTORY : ShellIconNativeMethods.FILE_ATTRIBUTE_NORMAL,
                                                ShellIconNativeMethods.SHGFI_USEFILEATTRIBUTES);
    }

    // Generic answer for non-existent paths and virtual items: the extension class from the image list, and
    // if that has nothing, the legacy 32px SHGetFileInfo bitmap.
    private static ImageSource? TryGetGenericIcon(bool isDir, string ext)
    {
        var attributes = isDir ? ShellIconNativeMethods.FILE_ATTRIBUTE_DIRECTORY : ShellIconNativeMethods.FILE_ATTRIBUTE_NORMAL;
        var lookupPath = isDir ? "dummy_folder" : "dummy" + ext;

        var hiRes = ShellImageListInterop.TryGetIcon(lookupPath, attributes, ShellIconNativeMethods.SHGFI_USEFILEATTRIBUTES);
        if (hiRes != null)
            return hiRes;

        var shfi = new ShellIconNativeMethods.SHFILEINFOW();
        var res = ShellIconNativeMethods.SHGetFileInfoW(lookupPath, attributes, ref shfi, (uint)Marshal.SizeOf(shfi),
                                                        ShellIconNativeMethods.SHGFI_ICON | ShellIconNativeMethods.SHGFI_LARGEICON | ShellIconNativeMethods.SHGFI_USEFILEATTRIBUTES);
        if (res == IntPtr.Zero || shfi.hIcon == IntPtr.Zero)
            return null;

        try
        {
            var bitmapSource = Imaging.CreateBitmapSourceFromHIcon(shfi.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            bitmapSource.Freeze();
            return bitmapSource;
        }
        finally
        {
            ShellIconNativeMethods.DestroyIcon(shfi.hIcon);
        }
    }

    public static ImageSource CreateVectorIcon(string pathData, string colorHexOrKey) => VectorIconFactory.Create(pathData, colorHexOrKey);
}
