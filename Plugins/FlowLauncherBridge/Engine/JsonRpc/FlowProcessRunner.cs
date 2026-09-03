using System.IO;
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
        var output = await FlowJsonRpcSession.RunProcessAsync(_executable, _scriptPath, _metadata, json, api, cancellationToken).ConfigureAwait(false);
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
        _ = await FlowJsonRpcSession.RunProcessAsync(_executable, _scriptPath, _metadata, json, api, CancellationToken.None).ConfigureAwait(false);
    }

    private IReadOnlyDictionary<string, object>? LoadPluginSettings()
    {
        var dict = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var baseDir = PluginSdk.Services.UserDataService.GetUserDataDirectory()
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lertaro");
            var pluginName = !string.IsNullOrEmpty(_metadata.Name) ? _metadata.Name : _metadata.ID;
            var settingsPath = FlowSettingsTemplateStorage.GetSettingsPath(baseDir, pluginName);
            if (File.Exists(settingsPath))
            {
                var json = File.ReadAllText(settingsPath);
                var loaded = JsonSerializer.Deserialize<Dictionary<string, object>>(json);
                if (loaded != null)
                {
                    foreach (var kv in loaded)
                        dict[kv.Key] = kv.Value;
                }
            }
        }
        catch { }

        var activeKeyword = FlowPluginStateStore.GetCustomKeyword(_metadata.Name)
            ?? (!string.IsNullOrWhiteSpace(_metadata.ActionKeyword) ? _metadata.ActionKeyword : string.Empty);
        if (!string.IsNullOrEmpty(activeKeyword))
        {
            dict["triggerKeyword"] = activeKeyword;
            dict["ActionKeyword"] = activeKeyword;
        }

        return dict.Count > 0 ? dict : null;
    }

    private List<Result> ParseResults(string output, IPublicAPI api)
    {
        var results = new List<Result>();
        try
        {
            var trimmed = output.Trim();
            if (string.IsNullOrEmpty(trimmed))
                return results;

            using var doc = JsonDocument.Parse(trimmed);
            var root = doc.RootElement;
            JsonElement targetArray = default;

            if (root.ValueKind == JsonValueKind.Array)
            {
                targetArray = root;
            }
            else if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("result", out var resElem) || root.TryGetProperty("Result", out resElem))
                {
                    if (resElem.ValueKind == JsonValueKind.Array)
                    {
                        targetArray = resElem;
                    }
                    else if (resElem.ValueKind == JsonValueKind.Object &&
                             (resElem.TryGetProperty("result", out var innerRes) || resElem.TryGetProperty("Result", out innerRes)) &&
                             innerRes.ValueKind == JsonValueKind.Array)
                    {
                        targetArray = innerRes;
                    }
                }
            }

            if (targetArray.ValueKind == JsonValueKind.Array)
            {
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                foreach (var elem in targetArray.EnumerateArray())
                {
                    var item = elem.Deserialize<JsonRpcResultItem>(options);
                    if (item != null)
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

        var autoText = item.AutoCompleteText ?? string.Empty;
        if (!string.IsNullOrEmpty(autoText) && api is FlowPublicApi flowApi)
            autoText = flowApi.NormalizeQueryWithKeyword(autoText);

        return new Result
        {
            Title = item.Title ?? string.Empty,
            SubTitle = item.SubTitle ?? string.Empty,
            IcoPath = icoPath ?? string.Empty,
            Score = item.Score,
            AutoCompleteText = autoText,
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
