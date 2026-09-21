using System.Collections;
using System.Reflection;
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
            if (field?.GetValue(provider) is IReadOnlyDictionary<IntPtr, string> map
                && map.TryGetValue(handle, out var path))
                return path;

            if (field?.GetValue(provider) is IDictionary legacyMap && legacyMap.Contains(handle))
                return GetPath(legacyMap[handle]);
        }
        catch (Exception ex)
        {
            Logger.Log($"[QuickNavigationPathResolver] Failed to reflect _nodeMap path: {ex.Message}", LogLevel.Error);
        }
        return null;
    }

    private static string? GetPath(object? value)
    {
        if (value is string path)
            return path;

        return value?.GetType().GetProperty("Path")?.GetValue(value) as string;
    }

    public static string? TryResolveCommandPath(IQuickNavigationProvider provider, uint commandId)
    {
        try
        {
            var field = provider.GetType().GetField("_commandMap", BindingFlags.NonPublic | BindingFlags.Instance);
            if (field?.GetValue(provider) is IReadOnlyDictionary<uint, string> map
                && map.TryGetValue(commandId, out var path))
                return path;

            if (field?.GetValue(provider) is IDictionary legacyMap && legacyMap.Contains(commandId))
                return legacyMap[commandId] as string;
        }
        catch { }
        return null;
    }
}
