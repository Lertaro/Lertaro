using System.IO;
using Lertaro.Core;
using Lertaro.App.ViewModels.Settings.Plugins;

namespace Lertaro.App.Services.PluginManagerCore;

/// <summary>
/// Manages the enabled/disabled state of individual plugin components.
/// Reads from persisted <see cref="UserSettings.DisabledPluginComponents"/> and exposes
/// a fast, thread-safe membership check used during filtering.
/// </summary>
internal class ComponentFilter
{
    private readonly HashSet<string> _disabledIds = new(StringComparer.OrdinalIgnoreCase);

    internal void Refresh()
    {
        try
        {
            var settings = UserSettings.Load();
            lock (_disabledIds)
            {
                _disabledIds.Clear();
                foreach (var id in settings.DisabledPluginComponents)
                    _disabledIds.Add(id);
            }
            Logger.Log($"[PluginManager] Refreshed disabled components. Count: {_disabledIds.Count}");
        }
        catch (Exception ex)
        {
            Logger.Log($"[PluginManager] Failed to refresh disabled components: {ex.Message}", LogLevel.Error);
        }
    }

    internal bool IsEnabled(string dllName, PluginComponentType type, string name)
    {
        var id = $"{dllName}::{type}::{name}";
        lock (_disabledIds)
            return !_disabledIds.Contains(id);
    }

    /// <summary>Returns the DLL filename for any loaded object, or empty string on failure.</summary>
    internal static string GetDllName(object obj)
    {
        try { return Path.GetFileName(obj.GetType().Assembly.Location); }
        catch { return string.Empty; }
    }
}
