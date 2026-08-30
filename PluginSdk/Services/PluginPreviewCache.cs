using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Controls;

namespace Lertaro.PluginSdk.Services;

/// <summary>
/// Cached metadata and lazy visual control for plugin-provided custom preview panels.
/// </summary>
public record PluginPreviewEntry(string Title, string PluginName, Lazy<UserControl> Factory, Func<object?>? IconProvider = null)
{
    private UIElement? _materialized;
    private object? _cachedIcon;
    private bool _iconLoaded;

    public UIElement? GetElement()
    {
        if (_materialized != null) return _materialized;
        try
        {
            _materialized = Factory.Value;
        }
        catch { }
        return _materialized;
    }

    public object? GetIcon()
    {
        if (_iconLoaded) return _cachedIcon;
        _iconLoaded = true;
        try
        {
            _cachedIcon = IconProvider?.Invoke();
        }
        catch { }
        return _cachedIcon;
    }
}

/// <summary>
/// Thread-safe in-memory cache for plugin-provided result preview panels.
/// </summary>
public static class PluginPreviewCache
{
    private const int MaxEntries = 100;
    private static readonly ConcurrentDictionary<string, PluginPreviewEntry> Entries = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentQueue<string> Keys = new();

    public static string Register(string title, string pluginName, Lazy<UserControl> factory, Func<object?>? iconProvider = null)
    {
        var encodedTitle = Uri.EscapeDataString(title);
        var encodedPlugin = Uri.EscapeDataString(pluginName);
        var id = $"flow-preview:{encodedTitle}:{encodedPlugin}:{Guid.NewGuid():N}";
        var entry = new PluginPreviewEntry(title, pluginName, factory, iconProvider);
        Entries[id] = entry;
        Keys.Enqueue(id);

        while (Keys.Count > MaxEntries && Keys.TryDequeue(out var oldKey))
        {
            Entries.TryRemove(oldKey, out _);
        }

        return id;
    }

    public static PluginPreviewEntry? GetEntry(string key)
    {
        if (string.IsNullOrEmpty(key)) return null;
        if (Entries.TryGetValue(key, out var entry))
            return entry;

        var parts = key.Split(':');
        if (parts.Length >= 4 && parts[0].Equals("flow-preview", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var title = Uri.UnescapeDataString(parts[1]);
                var plugin = Uri.UnescapeDataString(parts[2]);
                return new PluginPreviewEntry(title, plugin, new Lazy<UserControl>(() => new UserControl()));
            }
            catch (UriFormatException)
            {
                return null;
            }
        }

        return null;
    }

    public static UIElement? GetPreview(string key)
    {
        var entry = GetEntry(key);
        return entry?.GetElement();
    }
}
