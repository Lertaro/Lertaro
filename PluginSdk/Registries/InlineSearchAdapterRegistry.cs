using Lertaro.PluginSdk.Abstractions.Plugins.WindowAdapters;
namespace Lertaro.PluginSdk.Registries;

public static class InlineSearchAdapterRegistry
{
    private static readonly List<IInlineSearchAdapter> Adapters = new();

    /// <summary>
    /// Delegate to determine if an adapter is enabled.
    /// </summary>
    public static Func<IInlineSearchAdapter, bool> FilterFunc { get; set; } = _ => true;

    public static void Register(IInlineSearchAdapter adapter)
    {
        lock (Adapters)
        {
            if (!Adapters.Contains(adapter))
            {
                Adapters.Add(adapter);
            }
        }
    }

    public static IInlineSearchAdapter? GetMatchingAdapter(IntPtr hwnd, string className, string processName)
    {
        lock (Adapters)
        {
            foreach (var adapter in Adapters)
            {
                if (FilterFunc(adapter) && adapter.CanHandle(hwnd, className, processName))
                {
                    return adapter;
                }
            }
        }
        return null;
    }

    /// <summary>
    /// Retrieves only active (enabled) adapters.
    /// </summary>
    public static IReadOnlyList<IInlineSearchAdapter> GetAdapters()
    {
        lock (Adapters)
        {
            var active = new List<IInlineSearchAdapter>();
            foreach (var a in Adapters)
            {
                if (FilterFunc(a))
                {
                    active.Add(a);
                }
            }
            return active;
        }
    }

    /// <summary>
    /// Retrieves all registered adapters, regardless of enabled status.
    /// </summary>
    public static IReadOnlyList<IInlineSearchAdapter> GetAllAdapters()
    {
        lock (Adapters)
        {
            return Adapters.ToArray();
        }
    }
}
