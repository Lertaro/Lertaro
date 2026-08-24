using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using Flow.Launcher.Plugin;
using Lertaro.Plugins.FlowLauncherBridge.Engine.SettingsTemplate;

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
            Settings = LoadPluginSettings(),
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
        var method = action.Method;
        if (method.StartsWith("Flow.Launcher.", StringComparison.OrdinalIgnoreCase))
            method = method["Flow.Launcher.".Length..];

        if (string.Equals(method, "ChangeQuery", StringComparison.OrdinalIgnoreCase) && action.Parameters.Length > 0)
        {
            var query = action.Parameters[0]?.ToString() ?? string.Empty;
            var requery = action.Parameters.Length > 1 && (action.Parameters[1] is bool b ? b : (bool.TryParse(action.Parameters[1]?.ToString(), out var pb) && pb));
            api.ChangeQuery(query, requery);
            return;
        }

        if (string.Equals(method, "RestartApp", StringComparison.OrdinalIgnoreCase))
        {
            api.RestartApp();
            return;
        }

        if (string.Equals(method, "CopyToClipboard", StringComparison.OrdinalIgnoreCase) && action.Parameters.Length > 0)
        {
            var text = action.Parameters[0]?.ToString() ?? string.Empty;
            api.CopyToClipboard(text);
            return;
        }

        if ((string.Equals(method, "flow_open_url", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(method, "browser_open", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(method, "open_url", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(method, "OpenUrl", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(method, "OpenWebUrl", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(method, "OpenAppUri", StringComparison.OrdinalIgnoreCase)) && action.Parameters.Length > 0)
        {
            var url = action.Parameters[0]?.ToString();
            if (!string.IsNullOrEmpty(url)) { api.OpenUrl(url); return; }
        }

        if ((string.Equals(method, "flow_run_command", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(method, "ShellRun", StringComparison.OrdinalIgnoreCase)) && action.Parameters.Length > 0)
        {
            var cmd = action.Parameters[0]?.ToString();
            var filename = action.Parameters.Length > 1 ? action.Parameters[1]?.ToString() ?? "cmd.exe" : "cmd.exe";
            if (!string.IsNullOrEmpty(cmd)) { api.ShellRun(cmd, filename); return; }
        }

        if (string.Equals(method, "OpenDirectory", StringComparison.OrdinalIgnoreCase) && action.Parameters.Length > 0)
        {
            var dir = action.Parameters[0]?.ToString() ?? string.Empty;
            var file = action.Parameters.Length > 1 ? action.Parameters[1]?.ToString() : null;
            api.OpenDirectory(dir, file);
            return;
        }

        if (string.Equals(method, "OpenSettingDialog", StringComparison.OrdinalIgnoreCase))
        {
            api.OpenSettingDialog();
            return;
        }

        var request = new JsonRpcRequest
        {
            Method = action.Method,
            Parameters = action.Parameters,
            Settings = LoadPluginSettings(),
            Id = 2
        };

        var json = JsonSerializer.Serialize(request);
        _ = await RunProcessAsync(json, null, CancellationToken.None).ConfigureAwait(false);
    }

    private IReadOnlyDictionary<string, object>? LoadPluginSettings()
    {
        try
        {
            var baseDir = PluginSdk.Services.UserDataService.GetUserDataDirectory()
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lertaro");
            var pluginName = !string.IsNullOrEmpty(_metadata.Name) ? _metadata.Name : _metadata.ID;
            var settingsPath = FlowSettingsTemplateStorage.GetSettingsPath(baseDir, pluginName);
            if (File.Exists(settingsPath))
            {
                var json = File.ReadAllText(settingsPath);
                return JsonSerializer.Deserialize<Dictionary<string, object>>(json);
            }
        }
        catch { }
        return null;
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
            WindowStyle = ProcessWindowStyle.Hidden,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        if (!string.IsNullOrEmpty(_scriptPath))
        {
            psi.ArgumentList.Add(_scriptPath);
        }

        if (!string.IsNullOrEmpty(inputJson))
        {
            psi.ArgumentList.Add(inputJson);
        }

        psi.Environment["PYTHONIOENCODING"] = "utf-8";
        psi.Environment["PYTHONDONTWRITEBYTECODE"] = "1";
        psi.Environment["FLOW_VERSION"] = "1.19.0";
        psi.Environment["FLOW_PROGRAM_DIRECTORY"] = _metadata.PluginDirectory;
        psi.Environment["FLOW_APPLICATION_DIRECTORY"] = _metadata.PluginDirectory;
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
            process.StandardInput.Close();
        }
        catch { }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(15));

        try
        {
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            var completedTask = await Task.WhenAny(outputTask, Task.Delay(15000, cts.Token)).ConfigureAwait(false);
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
