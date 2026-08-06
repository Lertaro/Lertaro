using Lertaro.PluginSdk.Abstractions.Plugins.WindowAdapters;
namespace Lertaro.PluginSdk.Registries;

public static class FileDialogAdapterRegistry
{
    private static readonly List<IFileDialogAdapter> Adapters = new();

    /// <summary>
    /// Delegate to determine if an adapter is enabled.
    /// </summary>
    public static Func<IFileDialogAdapter, bool> FilterFunc { get; set; } = _ => true;

    public static void Register(IFileDialogAdapter adapter)
    {
        lock (Adapters)
        {
            if (!Adapters.Contains(adapter))
            {
                Adapters.Add(adapter);
            }
        }
    }

    public static IFileDialogAdapter? GetMatchingAdapter(IntPtr hwnd, string className, string processName)
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
    public static IReadOnlyList<IFileDialogAdapter> GetAdapters()
    {
        lock (Adapters)
        {
            var active = new List<IFileDialogAdapter>();
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
    public static IReadOnlyList<IFileDialogAdapter> GetAllAdapters()
    {
        lock (Adapters)
        {
            return Adapters.ToArray();
        }
    }
}
