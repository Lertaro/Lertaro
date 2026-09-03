using System.Text.Json.Serialization;

namespace Lertaro.Plugins.FlowLauncherBridge.Engine.JsonRpc;

/// <summary>
/// JSON-RPC request payload sent to external Flow.Launcher plugins (Python, Node.js, Executable).
/// </summary>
public sealed class JsonRpcRequest
{
    [JsonPropertyName("method")]
    public string Method { get; set; } = string.Empty;

    [JsonPropertyName("parameters")]
    public object[] Parameters { get; set; } = [];

    [JsonPropertyName("settings")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyDictionary<string, object>? Settings { get; set; }

    [JsonPropertyName("id")]
    public int Id { get; set; } = 1;
}

/// <summary>
/// JSON-RPC response payload received from external Flow.Launcher plugins.
/// </summary>
public sealed class JsonRpcResponse
{
    [JsonPropertyName("result")]
    public List<JsonRpcResultItem>? Result { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("id")]
    public int? Id { get; set; }
}

/// <summary>
/// Result item format returned by Flow.Launcher Python/Node/Executable plugins.
/// </summary>
public sealed class JsonRpcResultItem
{
    [JsonPropertyName("Title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("SubTitle")]
    public string? SubTitle { get; set; }

    [JsonPropertyName("IcoPath")]
    public string? IcoPath { get; set; }

    [JsonPropertyName("JsonRPCAction")]
    public JsonRpcActionModel? JsonRPCAction { get; set; }

    [JsonPropertyName("AutoCompleteText")]
    public string? AutoCompleteText { get; set; }

    [JsonPropertyName("Score")]
    public int Score { get; set; }
}

/// <summary>
/// JSON-RPC action payload attached to result items.
/// </summary>
public sealed class JsonRpcActionModel
{
    [JsonPropertyName("method")]
    public string Method { get; set; } = string.Empty;

    [JsonPropertyName("parameters")]
    public object[] Parameters { get; set; } = [];

    [JsonPropertyName("dontHideAfterAction")]
    public bool DontHideAfterAction { get; set; }
}
