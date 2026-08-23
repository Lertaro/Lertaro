namespace Flow.Launcher.Plugin;

public interface IFeatures
{
}

/// <summary>
/// Asynchronous plugin interface.
/// </summary>
public interface IAsyncPlugin : IFeatures
{
    Task<List<Result>> QueryAsync(Query query, CancellationToken token);
    Task InitAsync(PluginInitContext context);
}

/// <summary>
/// Synchronous plugin interface. Inherits from IAsyncPlugin with default interface methods.
/// </summary>
public interface IPlugin : IAsyncPlugin
{
    List<Result> Query(Query query);
    void Init(PluginInitContext context);

    Task IAsyncPlugin.InitAsync(PluginInitContext context) => Task.Run(() => Init(context));
    Task<List<Result>> IAsyncPlugin.QueryAsync(Query query, CancellationToken token) => Task.Run(() => Query(query), token);
}

public interface IContextMenu : IFeatures
{
    List<Result> LoadContextMenus(Result selectedResult);
}

public interface ISettingProvider : IFeatures
{
    System.Windows.Controls.Control CreateSettingPanel();
}

public interface ISavable : IFeatures
{
    void Save();
}

public interface IReloadable : IFeatures
{
    void ReloadData();
}

public interface IAsyncReloadable : IFeatures
{
    Task ReloadDataAsync();
}

public interface IPluginI18n : IFeatures
{
    string GetTranslatedPluginTitle();
    string GetTranslatedPluginDescription();
}

public interface IResultUpdated : IFeatures
{
    event ResultUpdatedEventHandler ResultsUpdated;
}

public interface IAsyncHomeQuery : IFeatures
{
    Task<List<Result>> HomeQueryAsync(CancellationToken token);
}

public interface IHomeQuery : IAsyncHomeQuery
{
    List<Result> HomeQuery();
    Task<List<Result>> IAsyncHomeQuery.HomeQueryAsync(CancellationToken token) => Task.Run(HomeQuery);
}
