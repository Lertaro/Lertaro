using System.IO;
using Flow.Launcher.Plugin;
using Lertaro.PluginSdk.Abstractions.Plugins;

namespace Lertaro.Plugins.FlowLauncherBridge.Engine;

/// <summary>
/// Resolves and attaches local Flow.Launcher plugin icons to instant result items.
/// </summary>
public static class FlowPluginIconHelper
{
    public static string? ResolvePluginIconPath(PluginMetadata? meta)
    {
        if (meta == null || string.IsNullOrWhiteSpace(meta.IcoPath))
            return null;

        if (Path.IsPathRooted(meta.IcoPath) && File.Exists(meta.IcoPath))
            return meta.IcoPath;

        if (!string.IsNullOrWhiteSpace(meta.PluginDirectory))
        {
            var combined = Path.Combine(meta.PluginDirectory, meta.IcoPath);
            if (File.Exists(combined))
                return combined;
        }

        return null;
    }

    public static void AttachPluginIcon(InstantResultItem item, PluginMetadata? meta)
    {
        var iconPath = ResolvePluginIconPath(meta);
        if (string.IsNullOrEmpty(iconPath))
            return;

        var hBitmap = FlowIconLoader.LoadIconAsHBitmap(iconPath);
        if (hBitmap != IntPtr.Zero)
        {
            item.HBitmapIcon = hBitmap;
        }
        else
        {
            item.IconData = "path:" + iconPath;
        }
    }
}
