namespace Lertaro.Core.Services.LocalSend;

/// <summary>
/// Builds LocalSend API routes while preserving the protocol's v1 compatibility paths.
/// </summary>
internal static class LocalSendApiRoute
{
    internal static bool UsesV1(string? version) => version == "1.0";

    internal static string GetPath(string endpoint, string? version) => (endpoint, UsesV1(version)) switch
    {
        ("prepare-upload", true) => "/api/localsend/v1/send-request",
        ("upload", true) => "/api/localsend/v1/send",
        (_, true) => $"/api/localsend/v1/{endpoint}",
        _ => $"/api/localsend/v2/{endpoint}"
    };

    internal static Uri BuildUri(string ip, int port, bool https, string endpoint, string? version)
    {
        var host = ip.Contains(':') ? $"[{ip.Trim('[', ']')}]" : ip;
        var scheme = https ? "https" : "http";
        return new Uri($"{scheme}://{host}:{port}{GetPath(endpoint, version)}");
    }
}
