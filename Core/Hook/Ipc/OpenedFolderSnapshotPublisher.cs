using Lertaro.PluginSdk.Registries;
using Lertaro.Core.Wire;
using Lertaro.PluginSdk.Abstractions.Plugins.WindowAdapters;

namespace Lertaro.Core.Hook.Ipc;

internal sealed class OpenedFolderSnapshotPublisher
{
    private readonly HookIpcServer _ipcServer;

    public OpenedFolderSnapshotPublisher(HookIpcServer ipcServer) => _ipcServer = ipcServer;

    public void Publish() => _ipcServer.SendMessage(new IpcMessage
    {
        Id = IpcMessageId.OpenedFoldersCaptured,
        StringList = OrderByWindowRank(OpenedFolderCollectorRegistry.GetOpenedFolders(), WindowRanks())
    });

    /// <summary>
    /// Orders snapshot paths by desktop window Z order, retaining each window's collector order through a
    /// stable sort. Unknown handles stay at the end in their original order.
    ///
    /// ponytail: Z order approximates activation history; topmost or recently minimized windows can be
    /// misplaced. A tracker-maintained activation list would improve this but covers only windows seen
    /// since Hook startup.
    /// </summary>
    internal static List<string> OrderByWindowRank(IEnumerable<OpenedFolder> folders, Func<IntPtr, int> rankOf) =>
        folders.Where(folder => !string.IsNullOrWhiteSpace(folder.Path))
            .OrderBy(folder => rankOf(folder.WindowHandle))
            .Select(folder => folder.Path)
            .ToList();

    /// <summary>
    /// Ranks every top-level window by Z order, normalised to its root owner so that one window's panes and tabs
    /// share a rank. A handle that is not there is ranked last.
    /// </summary>
    internal static Func<IntPtr, int> WindowRanks()
    {
        var ranks = new Dictionary<IntPtr, int>();
        var rank = 0;
        foreach (var window in ExplorerNativeHooks.TopLevelWindowsInZOrder())
            ranks.TryAdd(RootOf(window), rank++);

        return hwnd => hwnd != IntPtr.Zero && ranks.TryGetValue(RootOf(hwnd), out var found) ? found : int.MaxValue;
    }

    private static IntPtr RootOf(IntPtr hwnd)
    {
        var root = ExplorerNativeHooks.GetAncestor(hwnd, ExplorerNativeHooks.GA_ROOTOWNER);
        return root == IntPtr.Zero ? hwnd : root;
    }
}
