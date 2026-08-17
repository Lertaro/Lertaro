using Lertaro.PluginSdk.Abstractions.Plugins.WindowAdapters;

namespace Lertaro.PluginSdk.Registries;

/// <summary>
/// Gets opened-folder snapshots from enabled file-manager path collectors.
/// </summary>
public static class OpenedFolderCollectorRegistry
{
    /// <summary>
    /// Gets the folders each enabled collector currently exposes. Results intentionally retain duplicates:
    /// two windows or panes may show the same path, and a consumer can choose whether to collapse them.
    /// </summary>
    public static IReadOnlyList<OpenedFolder> GetOpenedFolders()
    {
        var folders = new List<OpenedFolder>();
        foreach (var collector in ActivePathCollectorRegistry.GetCollectors())
        {
            try
            {
                folders.AddRange(collector.GetOpenedFolders());
            }
            catch
            {
                // A single third-party file manager must not prevent a partial snapshot.
            }
        }
        return folders;
    }
}
