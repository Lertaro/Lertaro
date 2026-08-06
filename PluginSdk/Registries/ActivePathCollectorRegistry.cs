using Lertaro.PluginSdk.Abstractions.Plugins.WindowAdapters;
namespace Lertaro.PluginSdk.Registries;

/// <summary>
/// Registry for active path collectors loaded from plugins.
/// </summary>
public static class ActivePathCollectorRegistry
{
    private static readonly List<IActivePathCollector> Collectors = new();

    /// <summary>
    /// Delegate to determine if a path collector is enabled.
    /// </summary>
    public static Func<IActivePathCollector, bool> FilterFunc { get; set; } = _ => true;

    /// <summary>
    /// Registers a new active path collector.
    /// </summary>
    public static void Register(IActivePathCollector collector)
    {
        if (collector == null) return;
        lock (Collectors)
        {
            if (!Collectors.Contains(collector))
            {
                Collectors.Add(collector);
            }
        }
    }

    /// <summary>
    /// Retrieves all active (enabled) path collectors.
    /// </summary>
    public static IReadOnlyList<IActivePathCollector> GetCollectors()
    {
        lock (Collectors)
        {
            var active = new List<IActivePathCollector>();
            foreach (var c in Collectors)
            {
                if (FilterFunc(c))
                {
                    active.Add(c);
                }
            }
            return active;
        }
    }

    /// <summary>
    /// Retrieves all registered path collectors, regardless of enabled status.
    /// </summary>
    public static IReadOnlyList<IActivePathCollector> GetAllCollectors()
    {
        lock (Collectors)
        {
            return Collectors.ToArray();
        }
    }
}
