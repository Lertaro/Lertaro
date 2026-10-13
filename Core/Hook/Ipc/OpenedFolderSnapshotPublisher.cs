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
    /// The snapshot's paths, most recently activated window first.
    /// </summary>
    /// <remarks>
    /// The order used to be the collectors' own: the registry concatenates them in DLL-name order, so a row's
    /// position said which plugin produced it and nothing about the user's windows. Inline search numbers the
    /// opened folders with Ctrl+1..9 (and the cascader lists them in this order), so with two file managers open
    /// the numbering pointed at whichever one a plugin happened to answer first -- the complaint PR #352
    /// brought -- while every collector but Explorer already enumerates its own windows in Z order.
    ///
    /// OrderBy is stable, deliberately: one window contributes several entries (Directory Opus lists its active
    /// tab first, then the other panels) and they share a handle, so their relative order has to survive.
    /// Sorting by NAME is what once dropped Opus's active tab into the middle of the list -- see
    /// FolderCascader's MenuBuilderContentExtensions.
    ///
    /// Handles the desktop no longer has -- another virtual desktop, a window closed since the collector ran --
    /// keep their relative order at the end rather than being dropped: those are still folders the user has open.
    ///
    /// ponytail: Z order is a proxy, not evidence. It is wrong for a topmost (always-on-top) file manager and
    /// for one that was just minimised, which costs a position at the top of the list rather than a wrong
    /// folder. The direct evidence -- the order windows were actually activated in -- is the tracker's provider
    /// list, which only covers windows activated since this Hook started.
    /// </remarks>
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
