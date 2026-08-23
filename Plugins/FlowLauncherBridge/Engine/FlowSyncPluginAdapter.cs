using Flow.Launcher.Plugin;

namespace Lertaro.Plugins.FlowLauncherBridge.Engine;

/// <summary>
/// Adapts synchronous Flow IPlugin into IAsyncPlugin for unified async execution.
/// </summary>
public class FlowSyncPluginAdapter : IAsyncPlugin, IContextMenu, ISettingProvider, ISavable, IReloadable, IDisposable
{
    private readonly IPlugin _syncPlugin;

    public FlowSyncPluginAdapter(IPlugin syncPlugin) => _syncPlugin = syncPlugin;

    public IPlugin InnerPlugin => _syncPlugin;

    public Task InitAsync(PluginInitContext context)
    {
        _syncPlugin.Init(context);
        return Task.CompletedTask;
    }

    public Task<List<Result>> QueryAsync(Query query, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var results = _syncPlugin.Query(query);
        return Task.FromResult(results ?? []);
    }

    public List<Result> LoadContextMenus(Result selectedResult)
    {
        if (_syncPlugin is IContextMenu contextMenu)
        {
            return contextMenu.LoadContextMenus(selectedResult) ?? [];
        }
        return [];
    }

    public System.Windows.Controls.Control? CreateSettingPanel()
    {
        if (_syncPlugin is ISettingProvider settingProvider)
        {
            return settingProvider.CreateSettingPanel();
        }
        return null;
    }

    System.Windows.Controls.Control ISettingProvider.CreateSettingPanel() => CreateSettingPanel() ?? new System.Windows.Controls.UserControl();

    public void Save()
    {
        if (_syncPlugin is ISavable savable)
        {
            savable.Save();
        }
    }

    public void ReloadData()
    {
        if (_syncPlugin is IReloadable reloadable)
        {
            reloadable.ReloadData();
        }
    }

    public void Dispose()
    {
        if (_syncPlugin is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
