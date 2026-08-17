using Lertaro.PluginSdk.Registries;
using Lertaro.Core.Wire;

namespace Lertaro.Core.Hook.Ipc;

internal sealed class OpenedFolderSnapshotPublisher
{
    private readonly HookIpcServer _ipcServer;

    public OpenedFolderSnapshotPublisher(HookIpcServer ipcServer) => _ipcServer = ipcServer;

    public void Publish()
    {
        var paths = OpenedFolderCollectorRegistry.GetOpenedFolders()
            .Select(folder => folder.Path)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToList();
        _ipcServer.SendMessage(new IpcMessage { Id = IpcMessageId.OpenedFoldersCaptured, StringList = paths });
    }
}
