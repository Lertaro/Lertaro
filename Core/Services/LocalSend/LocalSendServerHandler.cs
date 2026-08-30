using System.Net;
using System.Text;

namespace Lertaro.Core.Services.LocalSend;

/// <summary>
/// Minimal HTTP request parser and router for the LocalSend TCP server.
/// Split out purely to keep LocalSendServer.cs under the repo's per-file line limit.
/// Has no state of its own; all routing delegates back to the LocalSendServer that owns it.
/// </summary>
internal static class LocalSendServerHandler
{
    private const int MaxRequestBodyBytes = 1024 * 1024;
    private const int MaxRequestLineBytes = 8192;

    internal static Task ProcessAsync(
        LocalSendServer server, Stream stream, EndPoint? remoteEp, string? peerFingerprint, CancellationToken token) =>
        LocalSendHttpConnection.ProcessAsync(server, stream, remoteEp, peerFingerprint, token);

    internal static async Task<bool> ProcessRequestAsync(
        LocalSendServer server, Stream stream, EndPoint? remoteEp, string? peerFingerprint, CancellationToken token)
    {
        // Read request line
        var requestLine = await ReadLineAsync(stream, token).ConfigureAwait(false);
        if (string.IsNullOrEmpty(requestLine))
            return false;

        var parts = requestLine.Split(' ');
        if (parts.Length < 2)
            return false;

        var method = parts[0];
        var fullPath = parts[1];

        // Split path and query string
        var qIdx = fullPath.IndexOf('?');
        var path = qIdx >= 0 ? fullPath[..qIdx] : fullPath;
        var queryRaw = qIdx >= 0 ? fullPath[(qIdx + 1)..] : string.Empty;
        var query = ParseQuery(queryRaw);

        // Read headers
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string line;
        while (!string.IsNullOrEmpty(line = await ReadLineAsync(stream, token).ConfigureAwait(false)))
        {
            var colon = line.IndexOf(':');
            if (colon > 0)
                headers[line[..colon].Trim()] = line[(colon + 1)..].Trim();
        }
        var keepAlive = ShouldKeepAlive(parts.Length > 2 ? parts[2] : "HTTP/1.0", headers);

        // Fingerprint self-check for GET /info
        if (method == "GET" && IsInfo(path))
        {
            var fp = query.GetValueOrDefault("fingerprint");
            if (!string.IsNullOrEmpty(fp) && fp == server.DeviceInfo.Fingerprint)
            {
                await LocalSendServerHelper.WriteResponseAsync(stream, 412, "{\"message\":\"Self-discovered\"}").ConfigureAwait(false);
                return keepAlive;
            }

            await LocalSendServerHelper.WriteResponseAsync(
                stream, 200, System.Text.Json.JsonSerializer.Serialize(LocalSendProtocolMapper.CreateInfo(server.DeviceInfo)))
                .ConfigureAwait(false);
            return keepAlive;
        }

        if (method != "POST")
        {
            await LocalSendServerHelper.WriteResponseAsync(stream, 404).ConfigureAwait(false);
            return keepAlive;
        }

        headers.TryGetValue("Content-Length", out var lenStr);
        long.TryParse(lenStr ?? "0", out var contentLength);
        using Stream requestBody = IsChunked(headers) ? new ChunkedReadStream(stream) : new LengthLimitedStream(stream, Math.Max(contentLength, 0));

        if (IsUpload(path))
        {
            // For uploads (including 0-byte empty files), stream body directly.
            await RouteUploadAsync(server, stream, requestBody, path, query, remoteEp, token).ConfigureAwait(false);
            await DrainBodyAsync(requestBody, token).ConfigureAwait(false);
            return keepAlive;
        }

        if (IsShow(path))
        {
            await RouteShowAsync(server, stream, query, requestBody, contentLength > 0 || IsChunked(headers), token).ConfigureAwait(false);
            return keepAlive;
        }

        // Read body for POST
        var bodyText = string.Empty;
        if (contentLength > 0 || IsChunked(headers))
            bodyText = await ReadBodyAsync(requestBody, token).ConfigureAwait(false);

        await RoutePostAsync(server, stream, path, query, bodyText, remoteEp, peerFingerprint, token).ConfigureAwait(false);
        return keepAlive;
    }

    private static async Task RoutePostAsync(
        LocalSendServer server, Stream stream, string path,
        Dictionary<string, string> query, string body, EndPoint? remoteEp, string? peerFingerprint, CancellationToken token)
    {
        if (IsRegister(path))
        {
            await LocalSendServerHelper.HandleRegisterAsync(server, stream, body, remoteEp, peerFingerprint).ConfigureAwait(false);
        }
        else if (IsPrepareUpload(path))
        {
            await LocalSendPrepareUploadHandler.HandleAsync(
                server, stream, query, body, remoteEp, peerFingerprint,
                path.Contains("/v2/", StringComparison.OrdinalIgnoreCase), token).ConfigureAwait(false);
        }
        else if (IsCancel(path))
        {
            query.TryGetValue("sessionId", out var sessionId);
            var senderIp = remoteEp is IPEndPoint ep ? LocalSendServerHelper.FormatIpAddress(ep.Address) : string.Empty;
            var v2 = path.Equals("/api/localsend/v2/cancel", StringComparison.OrdinalIgnoreCase);
            var canceled = LocalSendSessionAuthorization.TryCancel(server, sessionId, senderIp, v2);
            var accepted = v2 || canceled;
            await LocalSendServerHelper.WriteResponseAsync(stream, accepted ? 200 : 403, accepted ? null : "{\"message\":\"No permission\"}").ConfigureAwait(false);
        }
        else
        {
            await LocalSendServerHelper.WriteResponseAsync(stream, 404).ConfigureAwait(false);
        }
    }

    private static async Task RouteUploadAsync(
        LocalSendServer server, Stream stream, Stream requestBody, string path,
        Dictionary<string, string> query, EndPoint? remoteEp, CancellationToken token)
    {
        if (!IsUpload(path))
        {
            await LocalSendServerHelper.WriteResponseAsync(stream, 404).ConfigureAwait(false);
            return;
        }

        query.TryGetValue("sessionId", out var sessionId);
        query.TryGetValue("fileId", out var fileId);
        query.TryGetValue("token", out var tok);
        var senderIp = remoteEp is IPEndPoint ep ? LocalSendServerHelper.FormatIpAddress(ep.Address) : string.Empty;
        var v2 = path.Equals("/api/localsend/v2/upload", StringComparison.OrdinalIgnoreCase);

        if (string.IsNullOrEmpty(fileId) || string.IsNullOrEmpty(tok) || (v2 && string.IsNullOrEmpty(sessionId)))
        {
            await LocalSendServerHelper.WriteResponseAsync(stream, 400, "{\"message\":\"Missing parameters\"}").ConfigureAwait(false);
            return;
        }

        await server.HandleUploadAsync(stream, requestBody, sessionId ?? string.Empty, fileId ?? string.Empty, tok ?? string.Empty, senderIp, v2)
            .ConfigureAwait(false);
    }

    // ---- helpers ----

    private static bool IsInfo(string p) =>
        p.Equals("/api/localsend/v2/info", StringComparison.OrdinalIgnoreCase) ||
        p.Equals("/api/localsend/v1/info", StringComparison.OrdinalIgnoreCase);

    private static bool IsRegister(string p) =>
        p.Equals("/api/localsend/v2/register", StringComparison.OrdinalIgnoreCase) ||
        p.Equals("/api/localsend/v1/register", StringComparison.OrdinalIgnoreCase);

    private static bool IsPrepareUpload(string p) =>
        p.Equals("/api/localsend/v2/prepare-upload", StringComparison.OrdinalIgnoreCase) ||
        p.Equals("/api/localsend/v1/send-request", StringComparison.OrdinalIgnoreCase);

    private static bool IsUpload(string p) =>
        p.Equals("/api/localsend/v2/upload", StringComparison.OrdinalIgnoreCase) ||
        p.Equals("/api/localsend/v1/send", StringComparison.OrdinalIgnoreCase);

    private static bool IsCancel(string p) =>
        p.Equals("/api/localsend/v2/cancel", StringComparison.OrdinalIgnoreCase) ||
        p.Equals("/api/localsend/v1/cancel", StringComparison.OrdinalIgnoreCase);

    private static bool IsShow(string p) =>
        p.Equals("/api/localsend/v2/show", StringComparison.OrdinalIgnoreCase) ||
        p.Equals("/api/localsend/v1/show", StringComparison.OrdinalIgnoreCase);

    private static Dictionary<string, string> ParseQuery(string raw)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(raw)) return result;
        foreach (var pair in raw.Split('&'))
        {
            var eq = pair.IndexOf('=');
            if (eq > 0)
                result[Uri.UnescapeDataString(pair[..eq])] = Uri.UnescapeDataString(pair[(eq + 1)..]);
        }

        return result;
    }

    private static async Task RouteShowAsync(LocalSendServer server, Stream stream, Dictionary<string, string> query, Stream requestBody, bool hasBody, CancellationToken token)
    {
        if (query.GetValueOrDefault("token") != server.ShowToken)
        {
            await LocalSendServerHelper.WriteResponseAsync(stream, 403, "{\"message\":\"Invalid token\"}").ConfigureAwait(false);
            return;
        }

        await LocalSendServerHelper.WriteResponseAsync(stream, 200).ConfigureAwait(false);
        var body = hasBody ? await ReadBodyAsync(requestBody, token).ConfigureAwait(false) : string.Empty;
        server.InvokeShowRequested(LocalSendShowRequest.ParseFiles(body));
    }

    private static bool IsChunked(IReadOnlyDictionary<string, string> headers) =>
        headers.TryGetValue("Transfer-Encoding", out var transferEncoding) &&
        transferEncoding.Split(',').Any(value => value.Trim().Equals("chunked", StringComparison.OrdinalIgnoreCase));

    private static bool ShouldKeepAlive(string version, IReadOnlyDictionary<string, string> headers)
    {
        var connection = headers.GetValueOrDefault("Connection");
        if (connection?.Contains("close", StringComparison.OrdinalIgnoreCase) == true)
            return false;
        return !version.Equals("HTTP/1.0", StringComparison.OrdinalIgnoreCase) ||
            connection?.Contains("keep-alive", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static Task DrainBodyAsync(Stream body, CancellationToken token) => body.CopyToAsync(Stream.Null, token);

    private static async Task<string> ReadBodyAsync(Stream body, CancellationToken token)
    {
        using var memory = new MemoryStream();
        var buffer = new byte[81920];
        var total = 0;
        while (true)
        {
            var read = await body.ReadAsync(buffer.AsMemory(0, buffer.Length), token).ConfigureAwait(false);
            if (read == 0) break;
            total += read;
            if (total > MaxRequestBodyBytes)
                throw new InvalidDataException("Request body exceeds the maximum allowed size.");
            await memory.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
        }
        return Encoding.UTF8.GetString(memory.GetBuffer(), 0, (int)memory.Length);
    }

    private static async Task<string> ReadLineAsync(Stream stream, CancellationToken token)
    {
        var sb = new StringBuilder();
        var buf = new byte[1];
        while (true)
        {
            var read = await stream.ReadAsync(buf.AsMemory(0, 1), token).ConfigureAwait(false);
            if (read == 0) break;
            var ch = (char)buf[0];
            if (ch == '\n') break;
            if (ch != '\r')
                {
                    if (sb.Length >= MaxRequestLineBytes)
                        throw new InvalidDataException("Request line exceeds the maximum allowed length.");
                    sb.Append(ch);
                }
        }

        return sb.ToString();
    }
}
