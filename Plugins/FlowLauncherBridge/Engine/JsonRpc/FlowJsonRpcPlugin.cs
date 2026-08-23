using Flow.Launcher.Plugin;

namespace Lertaro.Plugins.FlowLauncherBridge.Engine.JsonRpc;

/// <summary>
/// Plugin adapter hosting external Flow.Launcher plugins (Python, Node.js, Executable).
/// </summary>
public class FlowJsonRpcPlugin : IAsyncPlugin
{
    private readonly FlowProcessRunner _runner;
    private IPublicAPI? _api;

    public FlowJsonRpcPlugin(FlowProcessRunner runner) => _runner = runner;

    public Task InitAsync(PluginInitContext context)
    {
        _api = context.API;
        return Task.CompletedTask;
    }

    public Task<List<Result>> QueryAsync(Query query, CancellationToken token)
    {
        if (_api == null)
            return Task.FromResult(new List<Result>());

        return _runner.ExecuteQueryAsync(query, _api, token);
    }
}
