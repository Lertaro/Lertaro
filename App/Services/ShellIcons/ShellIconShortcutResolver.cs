using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Lertaro.App.Services.ShellIcons;

public static class ShellIconShortcutResolver
{
    public static ImageSource? TryGetShortcutTargetIcon(string shortcutPath)
    {
        object? shellLinkObject = null;
        try
        {
            shellLinkObject = new ShellIconNativeMethods.ShellLink();
            var shellLink = (ShellIconNativeMethods.IShellLinkW)shellLinkObject;
            var persistFile = (IPersistFile)shellLinkObject;
            persistFile.Load(shortcutPath, 0);

            var iconPathBuilder = new StringBuilder(ShellIconNativeMethods.MAX_PATH);
            shellLink.GetIconLocation(iconPathBuilder, iconPathBuilder.Capacity, out var iconIndex);
            var iconPath = Environment.ExpandEnvironmentVariables(iconPathBuilder.ToString());

            if (!string.IsNullOrWhiteSpace(iconPath) && File.Exists(iconPath))
            {
                var icon = ExtractLargeIcon(iconPath, iconIndex);
                if (icon != null)
                {
                    return icon;
                }
            }

            var targetPathBuilder = new StringBuilder(ShellIconNativeMethods.MAX_PATH);
            shellLink.GetPath(targetPathBuilder, targetPathBuilder.Capacity, IntPtr.Zero, ShellIconNativeMethods.SLGP_UNCPRIORITY);
            var targetPath = Environment.ExpandEnvironmentVariables(targetPathBuilder.ToString());
            if (!string.IsNullOrWhiteSpace(targetPath) && File.Exists(targetPath))
            {
                if (targetPath.EndsWith(".msc", StringComparison.OrdinalIgnoreCase))
                {
                    var mscIcon = TryGetMscIcon(targetPath);
                    if (mscIcon != null) return mscIcon;
                }
                return ExtractLargeIcon(targetPath, 0) ?? GetShellIconWithoutLinkOverlay(targetPath);
            }
        }
        catch (Exception ex)
        {
            Core.Logger.Log($"[ShellIconHelper] Failed to resolve shortcut icon for {shortcutPath}: {ex.Message}", Core.LogLevel.Warn);
        }
        finally
        {
            if (shellLinkObject != null)
            {
                Marshal.FinalReleaseComObject(shellLinkObject);
            }
        }

        return null;
    }

    internal static ImageSource? ExtractLargeIcon(string iconPath, int iconIndex)
    {
        // Prefer a high-resolution extraction (48/256px) so shortcut icons stay crisp.
        var hiRes = ShellImageListInterop.ExtractHiRes(iconPath, iconIndex);
        if (hiRes != null)
        {
            return hiRes;
        }

        var largeIcons = new IntPtr[1];
        var extracted = ShellIconNativeMethods.ExtractIconEx(iconPath, iconIndex, largeIcons, null, 1);
        if (extracted == 0 || largeIcons[0] == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var bitmapSource = Imaging.CreateBitmapSourceFromHIcon(
                largeIcons[0],
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            bitmapSource.Freeze();
            return bitmapSource;
        }
        finally
        {
            ShellIconNativeMethods.DestroyIcon(largeIcons[0]);
        }
    }

    public static ImageSource? GetShellIconWithoutLinkOverlay(string targetPath)
    {
        var hiRes = ShellImageListInterop.TryGetIcon(targetPath, 0, 0);
        if (hiRes != null)
        {
            return hiRes;
        }

        var shfi = new ShellIconNativeMethods.SHFILEINFOW();
        var res = ShellIconNativeMethods.SHGetFileInfoW(targetPath, 0, ref shfi, (uint)Marshal.SizeOf(shfi), ShellIconNativeMethods.SHGFI_ICON | ShellIconNativeMethods.SHGFI_LARGEICON);
        if (res == IntPtr.Zero || shfi.hIcon == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var bitmapSource = Imaging.CreateBitmapSourceFromHIcon(
                shfi.hIcon,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            bitmapSource.Freeze();
            return bitmapSource;
        }
        finally
        {
            ShellIconNativeMethods.DestroyIcon(shfi.hIcon);
        }
    }

    public static ImageSource? TryGetMscIcon(string mscPath)
    {
        var hIcon = PluginSdk.Helpers.ShellPathHelper.ExtractMscHIcon(mscPath, 96);
        if (hIcon == IntPtr.Zero) return null;
        try
        {
            var bitmapSource = Imaging.CreateBitmapSourceFromHIcon(
                hIcon,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            bitmapSource.Freeze();
            return bitmapSource;
        }
        catch (Exception ex)
        {
            Core.Logger.Log($"[ShellIconShortcutResolver] Failed to convert MSC icon from {mscPath}: {ex.Message}", Core.LogLevel.Warn);
            return null;
        }
        finally
        {
            ShellIconNativeMethods.DestroyIcon(hIcon);
        }
    }
}
