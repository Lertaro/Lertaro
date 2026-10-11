using Lertaro.PluginSdk.Registries;
using Lertaro.Core.Wire;

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
        var paths = OpenedFolderCollectorRegistry.GetOpenedFolders()
            .Where(folder => !string.IsNullOrWhiteSpace(folder.Path))
            .OrderBy(folder => ExplorerNativeHooks.ZOrderRank(folder.WindowHandle))
            .Select(folder => folder.Path)
            .ToList();
        _ipcServer.SendMessage(new IpcMessage { Id = IpcMessageId.OpenedFoldersCaptured, StringList = paths });
    }
}
