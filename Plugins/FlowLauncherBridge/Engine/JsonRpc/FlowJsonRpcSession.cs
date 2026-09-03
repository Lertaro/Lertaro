using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Flow.Launcher.Plugin;
using Flow.Launcher.Plugin.SharedModels;

namespace Lertaro.Plugins.FlowLauncherBridge.Engine.JsonRpc;

/// <summary>
/// Executes external Flow.Launcher plugins via subprocess stdin/stdout bidirectional JSON-RPC communication.
/// Split out from FlowProcessRunner to stay strictly under the per-file line limit.
/// </summary>
public static class FlowJsonRpcSession
{
    public static async Task<string> RunProcessAsync(
        string executable,
        string? scriptPath,
        PluginMetadata metadata,
        string inputJson,
        IPublicAPI? api,
        CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = metadata.PluginDirectory,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        if (!string.IsNullOrEmpty(scriptPath))
        {
            psi.ArgumentList.Add(scriptPath);
        }

        if (!string.IsNullOrEmpty(inputJson))
        {
            psi.ArgumentList.Add(inputJson);
        }

        psi.Environment["PYTHONIOENCODING"] = "utf-8";
        psi.Environment["PYTHONDONTWRITEBYTECODE"] = "1";
        psi.Environment["FLOW_VERSION"] = "1.19.0";
        psi.Environment["FLOW_PROGRAM_DIRECTORY"] = metadata.PluginDirectory;
        psi.Environment["FLOW_APPLICATION_DIRECTORY"] = metadata.PluginDirectory;
        psi.Environment["FLOW_LAUNCHER_SETTINGS_PATH"] = metadata.PluginSettingsDirectoryPath;

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
        cts.CancelAfter(TimeSpan.FromSeconds(15));

        var finalOutput = string.Empty;
        try
        {
            while (!process.HasExited && !cts.Token.IsCancellationRequested)
            {
                var readLineTask = process.StandardOutput.ReadLineAsync(cts.Token).AsTask();
                var completed = await Task.WhenAny(readLineTask, Task.Delay(15000, cts.Token)).ConfigureAwait(false);
                if (completed != readLineTask)
                    break;

                var line = await readLineTask.ConfigureAwait(false);
                if (line == null)
                    break;

                var trimmedLine = line.Trim();
                if (string.IsNullOrEmpty(trimmedLine))
                    continue;

                if (!trimmedLine.StartsWith('{') && !trimmedLine.StartsWith('['))
                    continue;

                try
                {
                    using var doc = JsonDocument.Parse(trimmedLine);
                    var root = doc.RootElement;
                    if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("method", out _))
                    {
                        await HandleIncomingRpcCallAsync(root, process, api).ConfigureAwait(false);
                        continue;
                    }

                    finalOutput = trimmedLine;
                    break;
                }
                catch (JsonException)
                {
                    // Non-JSON stdout line, ignore and continue
                }
            }
        }
        catch { }

        // Send close notification to allow clean shutdown for python_v2 plugins
        try
        {
            if (!process.HasExited)
            {
                await process.StandardInput.WriteLineAsync("{\"id\":999,\"method\":\"close\"}").ConfigureAwait(false);
                await process.StandardInput.FlushAsync().ConfigureAwait(false);
                process.StandardInput.Close();

                if (!process.WaitForExit(300))
                {
                    process.Kill();
                }
            }
        }
        catch
        {
            try { process.Kill(); } catch { }
        }

        return finalOutput;
    }

    private static async Task HandleIncomingRpcCallAsync(JsonElement root, Process process, IPublicAPI? api)
    {
        if (!root.TryGetProperty("id", out var idElem))
            return;

        var reqId = idElem.GetInt64();
        var method = root.TryGetProperty("method", out var mElem) ? mElem.GetString() ?? string.Empty : string.Empty;

        if (string.Equals(method, "FuzzySearch", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(method, "Flow.Launcher.FuzzySearch", StringComparison.OrdinalIgnoreCase))
        {
            var matchResult = HandleFuzzySearch(root, api);
            var resp = JsonSerializer.Serialize(new
            {
                id = reqId,
                result = new
                {
                    success = matchResult.Success,
                    score = matchResult.Score,
                    searchPrecision = (int)matchResult.SearchPrecision,
                    matchData = matchResult.MatchData
                }
            });
            await process.StandardInput.WriteLineAsync(resp).ConfigureAwait(false);
            await process.StandardInput.FlushAsync().ConfigureAwait(false);
            return;
        }

        HandleOtherRpcCall(method, root, api);
        var ack = JsonSerializer.Serialize(new { id = reqId, result = (object?)null });
        await process.StandardInput.WriteLineAsync(ack).ConfigureAwait(false);
        await process.StandardInput.FlushAsync().ConfigureAwait(false);
    }

    internal static MatchResult HandleFuzzySearch(JsonElement root, IPublicAPI? api)
    {
        if (api == null)
            return new MatchResult(false, SearchPrecisionScore.Regular);

        var query = string.Empty;
        var text = string.Empty;

        if (root.TryGetProperty("params", out var pElem) && pElem.ValueKind == JsonValueKind.Array)
        {
            var array = pElem.EnumerateArray().ToList();
            if (array.Count > 0) query = array[0].GetString() ?? string.Empty;
            if (array.Count > 1) text = array[1].GetString() ?? string.Empty;
        }

        return api.FuzzySearch(query, text);
    }

    internal static void HandleOtherRpcCall(string method, JsonElement root, IPublicAPI? api)
    {
        if (api == null) return;
        if (method.StartsWith("Flow.Launcher.", StringComparison.OrdinalIgnoreCase))
            method = method["Flow.Launcher.".Length..];

        var paramsList = new List<string>();
        if (root.TryGetProperty("params", out var pElem) && pElem.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in pElem.EnumerateArray())
                paramsList.Add(item.ToString() ?? string.Empty);
        }

        if (string.Equals(method, "CopyToClipboard", StringComparison.OrdinalIgnoreCase) && paramsList.Count > 0)
        {
            api.CopyToClipboard(paramsList[0]);
        }
        else if ((string.Equals(method, "OpenUrl", StringComparison.OrdinalIgnoreCase) ||
                  string.Equals(method, "OpenWebUrl", StringComparison.OrdinalIgnoreCase) ||
                  string.Equals(method, "OpenAppUri", StringComparison.OrdinalIgnoreCase)) && paramsList.Count > 0)
        {
            api.OpenUrl(paramsList[0]);
        }
        else if (string.Equals(method, "ShellRun", StringComparison.OrdinalIgnoreCase) && paramsList.Count > 0)
        {
            var cmd = paramsList[0];
            var fn = paramsList.Count > 1 ? paramsList[1] : "cmd.exe";
            api.ShellRun(cmd, fn);
        }
        else if (string.Equals(method, "ChangeQuery", StringComparison.OrdinalIgnoreCase) && paramsList.Count > 0)
        {
            var requery = paramsList.Count > 1 && bool.TryParse(paramsList[1], out var b) && b;
            api.ChangeQuery(paramsList[0], requery);
        }
        else if (string.Equals(method, "RestartApp", StringComparison.OrdinalIgnoreCase))
        {
            api.RestartApp();
        }
    }
}
