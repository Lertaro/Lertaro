using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
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
    private static readonly ConditionalWeakTable<PluginPreviewEntry, string> EntryKeys = new();
    private static readonly ConcurrentDictionary<string, WeakReference<PluginPreviewEntry>> Entries = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentQueue<PluginPreviewEntry> RecentEntries = new();
    private static int _registrations;

    public static string Register(string title, string pluginName, Lazy<UserControl> factory, Func<object?>? iconProvider = null) =>
        Register(new PluginPreviewEntry(title, pluginName, factory, iconProvider));

    /// <summary>Reuses an entry's key. Keep the entry alive for as long as its result can be displayed.</summary>
    public static string Register(PluginPreviewEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var id = EntryKeys.GetValue(entry, e => $"flow-preview:{Uri.EscapeDataString(e.Title)}:{Uri.EscapeDataString(e.PluginName)}:{Guid.NewGuid():N}");
        if (!Entries.TryAdd(id, new WeakReference<PluginPreviewEntry>(entry)))
            return id;

        // Retain recent string-only registrations; live result owners retain older entries themselves.
        RecentEntries.Enqueue(entry);
        while (RecentEntries.Count > MaxEntries && RecentEntries.TryDequeue(out _)) { }

        // ponytail: scan O(n) keys every 100 registrations; use incremental pruning if live result sets grow large.
        if (Interlocked.Increment(ref _registrations) % MaxEntries == 0)
            foreach (var (key, reference) in Entries)
                if (!reference.TryGetTarget(out _)) Entries.TryRemove(key, out _);

        return id;
    }

    public static PluginPreviewEntry? GetEntry(string key)
    {
        if (string.IsNullOrEmpty(key)) return null;
        if (Entries.TryGetValue(key, out var reference) && reference.TryGetTarget(out var entry))
            return entry;

        return null;
    }

    public static UIElement? GetPreview(string key)
    {
        var entry = GetEntry(key);
        return entry?.GetElement();
    }
}
