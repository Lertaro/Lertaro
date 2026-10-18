using Lertaro.PluginSdk.Registries;
using Lertaro.Core.Wire;
using Lertaro.PluginSdk.Abstractions.Plugins.WindowAdapters;

namespace Lertaro.Core.Hook.Ipc;

internal sealed class OpenedFolderSnapshotPublisher
{
    private readonly HookIpcServer _ipcServer;

    public OpenedFolderSnapshotPublisher(HookIpcServer ipcServer) => _ipcServer = ipcServer;

    public void Publish()
    {
        // Published in "last activated first" order: the inline search numbers these entries Ctrl+1, Ctrl+2,
        // ..., and the most recently used file manager window is the one the user expects on Ctrl+1. The IPC
        // carries paths only, so the ordering has to be decided here, where the window handles still exist.
        // Desktop sources are dropped here too, so the App-side list is clean without its own filter.
        var paths = OrderByWindowRank(
            OpenedFolderCollectorRegistry.GetOpenedFolders()
                .Where(folder => InlineSearch.QuickSwitchTargetResolver.IsFileManagerWindow(folder.WindowHandle)),
            ExplorerNativeHooks.ZOrderRank);
        _ipcServer.SendMessage(new IpcMessage { Id = IpcMessageId.OpenedFoldersCaptured, StringList = paths });
    }

    // OrderBy is stable on purpose: one window contributes several entries (Directory Opus lists its active tab
    // before its other panels) and they share a handle, so their relative order has to survive. Handles the
    // desktop no longer has keep their relative order at the end rather than being dropped -- ZOrderRank
    // answers int.MaxValue for those -- they are still folders the user has open. Split out so the ordering is
    // testable without a live desktop.
    internal static List<string> OrderByWindowRank(IEnumerable<OpenedFolder> folders, Func<IntPtr, int> rankOf) =>
        folders.Where(folder => !string.IsNullOrWhiteSpace(folder.Path))
            .OrderBy(folder => rankOf(folder.WindowHandle))
            .Select(folder => folder.Path)
            .ToList();

    // The real rank table, over this desktop's top-level windows. Shares ZOrderRank with QuickSwitch, so the
    // opened-folder order and the Quick Switch target order cannot drift apart.
    internal static Func<IntPtr, int> WindowRanks() => ExplorerNativeHooks.ZOrderRank;
}
