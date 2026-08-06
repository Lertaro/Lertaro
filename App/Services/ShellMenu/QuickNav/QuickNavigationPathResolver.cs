using System.Reflection;
using System.Collections;
using Logger = Lertaro.Core.Logger;
using LogLevel = Lertaro.Core.LogLevel;

using Lertaro.PluginSdk.Abstractions.Plugins.WindowAdapters;
namespace Lertaro.App.Services.ShellMenu.QuickNav;

public static class QuickNavigationPathResolver
{
    public static string? TryResolveSubMenuPath(IQuickNavigationProvider provider, IntPtr handle)
    {
        try
        {
            var field = provider.GetType().GetField("_nodeMap", BindingFlags.NonPublic | BindingFlags.Instance);
            if (field != null && field.GetValue(provider) is IDictionary map && map.Contains(handle))
            {
                var val = map[handle];
                if (val is string path)
                {
                    return path;
                }
                else if (val != null)
                {
                    var prop = val.GetType().GetProperty("Path");
                    if (prop != null)
                    {
                        return prop.GetValue(val) as string;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"[QuickNavigationPathResolver] Failed to reflect _nodeMap path: {ex.Message}", LogLevel.Error);
        }
        return null;
    }

    public static string? TryResolveCommandPath(IQuickNavigationProvider provider, uint commandId)
    {
        try
        {
            var field = provider.GetType().GetField("_commandMap", BindingFlags.NonPublic | BindingFlags.Instance);
            if (field != null && field.GetValue(provider) is IDictionary map && map.Contains(commandId))
            {
                return map[commandId] as string;
            }
        }
        catch { }
        return null;
    }
}
