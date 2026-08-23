using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using Flow.Launcher.Plugin;

namespace Lertaro.Plugins.FlowLauncherBridge.Engine.JsonRpc;

/// <summary>
/// Executes external Flow.Launcher plugins via subprocess stdin/stdout JSON-RPC communication.
/// </summary>
public class FlowProcessRunner
{
    private readonly PluginMetadata _metadata;
    private readonly string _executable;
    private readonly string? _scriptPath;

    public FlowProcessRunner(PluginMetadata metadata, string executable, string? scriptPath = null)
    {
        _metadata = metadata;
        _executable = executable;
        _scriptPath = scriptPath;
    }

    public async Task<List<Result>> ExecuteQueryAsync(Query query, IPublicAPI api, CancellationToken cancellationToken = default)
    {
        var request = new JsonRpcRequest
        {
            Method = "query",
            Parameters = [query.Search],
            Id = 1
        };

        var json = JsonSerializer.Serialize(request);
        var output = await RunProcessAsync(json, query.Search, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(output))
            return [];

        return ParseResults(output, api);
    }

    public async Task ExecuteActionAsync(JsonRpcActionModel action, IPublicAPI api)
    {
        if (string.Equals(action.Method, "flow_open_url", StringComparison.OrdinalIgnoreCase) && action.Parameters.Length > 0)
        {
            var url = action.Parameters[0]?.ToString();
            if (!string.IsNullOrEmpty(url)) api.OpenUrl(url);
            return;
        }

        if (string.Equals(action.Method, "flow_run_command", StringComparison.OrdinalIgnoreCase) && action.Parameters.Length > 0)
        {
            var cmd = action.Parameters[0]?.ToString();
            if (!string.IsNullOrEmpty(cmd)) api.ShellRun(cmd);
            return;
        }

        var request = new JsonRpcRequest
        {
            Method = action.Method,
            Parameters = action.Parameters,
            Id = 2
        };

        var json = JsonSerializer.Serialize(request);
        _ = await RunProcessAsync(json, null, CancellationToken.None).ConfigureAwait(false);
    }

    private async Task<string> RunProcessAsync(string inputJson, string? cliQuery, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _executable,
            WorkingDirectory = _metadata.PluginDirectory,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        if (!string.IsNullOrEmpty(_scriptPath))
        {
            psi.ArgumentList.Add(_scriptPath);
        }

        if (!string.IsNullOrEmpty(cliQuery))
        {
            psi.ArgumentList.Add(cliQuery);
        }

        psi.Environment["PYTHONIOENCODING"] = "utf-8";
        psi.Environment["FLOW_LAUNCHER_SETTINGS_PATH"] = _metadata.PluginDirectory;

        using var process = new Process { StartInfo = psi };
        try
        {
            process.Start();
        }
        catch
        {
            return string.Empty;
        }

        try
        {
            if (!string.IsNullOrEmpty(inputJson))
            {
                await process.StandardInput.WriteLineAsync(inputJson).ConfigureAwait(false);
                await process.StandardInput.FlushAsync().ConfigureAwait(false);
            }
        }
        catch { }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(5));

        try
        {
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var completedTask = await Task.WhenAny(outputTask, Task.Delay(5000, cts.Token)).ConfigureAwait(false);
            if (completedTask == outputTask)
            {
                return await outputTask.ConfigureAwait(false);
            }

            try { process.Kill(); } catch { }
            return string.Empty;
        }
        catch
        {
            try { process.Kill(); } catch { }
            return string.Empty;
        }
    }

    private List<Result> ParseResults(string output, IPublicAPI api)
    {
        var results = new List<Result>();
        try
        {
            var trimmed = output.Trim();
            // JSON-RPC format could be {"result": [...]} or raw array [...]
            if (trimmed.StartsWith("{"))
            {
                var response = JsonSerializer.Deserialize<JsonRpcResponse>(trimmed, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (response?.Result != null)
                {
                    foreach (var item in response.Result)
                    {
                        results.Add(MapResult(item, api));
                    }
                }
            }
            else if (trimmed.StartsWith("["))
            {
                var items = JsonSerializer.Deserialize<List<JsonRpcResultItem>>(trimmed, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (items != null)
                {
                    foreach (var item in items)
                    {
                        results.Add(MapResult(item, api));
                    }
                }
            }
        }
        catch { }
        return results;
    }

    private Result MapResult(JsonRpcResultItem item, IPublicAPI api)
    {
        var icoPath = item.IcoPath;
        if (!string.IsNullOrEmpty(icoPath) && !Path.IsPathRooted(icoPath))
        {
            icoPath = Path.Combine(_metadata.PluginDirectory, icoPath);
        }

        return new Result
        {
            Title = item.Title ?? string.Empty,
            SubTitle = item.SubTitle ?? string.Empty,
            IcoPath = icoPath ?? string.Empty,
            AutoCompleteText = item.AutoCompleteText ?? string.Empty,
            AsyncAction = async _ =>
            {
                if (item.JsonRPCAction != null)
                {
                    try
                    {
                        await ExecuteActionAsync(item.JsonRPCAction, api).ConfigureAwait(false);
                    }
                    catch { }
                    return !item.JsonRPCAction.DontHideAfterAction;
                }
                return true;
            }
        };
    }
}
