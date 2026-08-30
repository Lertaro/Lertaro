using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace Lertaro.Core.Services.LocalSend;

/// <summary>
/// Helper methods for LocalSendServer to keep the main server class under 300 lines.
/// Split out purely to adhere to the repository's per-file line limit; has no internal state of its own.
/// </summary>
public static class LocalSendServerHelper
{
    /// <summary>
    /// Tries to create a dual-stack TcpListener (IPv6Any + DualMode=true) that accepts
    /// both IPv4 and IPv6 connections on a single socket. Returns null if IPv6 is
    /// unavailable on this host (DualMode not supported).
    /// </summary>
    internal static TcpListener? TryCreateDualStackListener(int port)
    {
        try
        {
            var listener = new TcpListener(IPAddress.IPv6Any, port);
            listener.Server.DualMode = true;
            return listener;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Formats an IPAddress cleanly, unmapping IPv4-mapped IPv6 addresses (e.g. ::ffff:192.168.1.1) to standard IPv4.
    /// </summary>
    internal static string FormatIpAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            return address.MapToIPv4().ToString();
        }

        return address.ToString();
    }

    /// <summary>
    /// Cleans an IP address string by stripping brackets and unmapping IPv4-mapped IPv6 prefixes.
    /// </summary>
    internal static string CleanIpAddress(string ip)
    {
        if (string.IsNullOrWhiteSpace(ip)) return ip;
        var trimmed = ip.Trim('[', ']');
        if (trimmed.StartsWith("::ffff:", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed.Substring(7);
        }
        if (IPAddress.TryParse(trimmed, out var parsed))
        {
            return FormatIpAddress(parsed);
        }
        return trimmed;
    }

    /// <summary>
    /// Writes an HTTP response line, headers, and optional JSON body to the network stream.
    /// </summary>
    internal static async Task WriteResponseAsync(Stream stream, int status, string? json = null)
    {
        var statusText = status switch
        {
            200 => "OK",
            204 => "No Content",
            401 => "Unauthorized",
            400 => "Bad Request",
            403 => "Forbidden",
            404 => "Not Found",
            409 => "Conflict",
            412 => "Precondition Failed",
            422 => "Unprocessable Entity",
            429 => "Too Many Requests",
            _ => "Internal Server Error"
        };

        var plainNotFound = status == 404 && json == null;
        var sb = new StringBuilder();
        sb.Append($"HTTP/1.1 {status} {statusText}\r\n");
        if (status == 204)
        {
            sb.Append("\r\n");
            await stream.WriteAsync(Encoding.UTF8.GetBytes(sb.ToString())).ConfigureAwait(false);
            await stream.FlushAsync().ConfigureAwait(false);
            return;
        }

        var body = Encoding.UTF8.GetBytes(plainNotFound ? "Not found" : json ?? "{}");
        sb.Append($"Content-Type: {(plainNotFound ? "text/plain" : "application/json")}; charset=utf-8\r\n");
        sb.Append($"Transfer-Encoding: chunked\r\n\r\n{body.Length:X}\r\n");
        var header = Encoding.UTF8.GetBytes(sb.ToString());
        await stream.WriteAsync(header).ConfigureAwait(false);
        await stream.WriteAsync(body).ConfigureAwait(false);
        await stream.WriteAsync(Encoding.UTF8.GetBytes("\r\n0\r\n\r\n")).ConfigureAwait(false);

        await stream.FlushAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Sends the cancel callback on the route selected by the sender's advertised LocalSend version.
    /// </summary>
    internal static async Task<bool> NotifySenderCanceledAsync(Models.LocalSendDeviceInfo senderInfo, string sessionId)
    {
        if (string.IsNullOrEmpty(senderInfo.IpAddress) || senderInfo.Port <= 0 || string.IsNullOrEmpty(sessionId))
            return false;

        var url = BuildCancellationUri(senderInfo, sessionId);
        try
        {
            using var identity = LocalSendCertificate.LoadOrCreate();
            using var client = LocalSendHttpClientFactory.Create(
                identity, senderInfo.Https ? senderInfo.Fingerprint : null, TimeSpan.FromSeconds(3));
            using var response = await client.PostAsync(url, null).ConfigureAwait(false);
            Logger.Log($"[LocalSendServer] Notified sender cancellation: {url} -> {response.StatusCode}", LogLevel.Debug);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Logger.Log($"[LocalSendServer] Sender cancellation notification error: {ex.Message}", LogLevel.Warn);
            return false;
        }
    }

    internal static string BuildCancellationUri(Models.LocalSendDeviceInfo senderInfo, string sessionId)
    {
        var uri = LocalSendApiRoute.BuildUri(CleanIpAddress(senderInfo.IpAddress), senderInfo.Port, senderInfo.Https, "cancel", senderInfo.Version).ToString();
        return LocalSendApiRoute.UsesV1(senderInfo.Version) ? uri : $"{uri}?sessionId={Uri.EscapeDataString(sessionId)}";
    }

    internal static async Task HandleRegisterAsync(LocalSendServer server, Stream stream, string body, EndPoint? remoteEp,
        string? peerFingerprint)
    {
        Models.LocalSendRegisterDto? registration;
        try { registration = System.Text.Json.JsonSerializer.Deserialize<Models.LocalSendRegisterDto>(body); }
        catch (System.Text.Json.JsonException)
        {
            await WriteResponseAsync(stream, 400, "{\"message\":\"Request body malformed\"}").ConfigureAwait(false);
            return;
        }

        if (registration == null || string.IsNullOrEmpty(registration.Alias) || string.IsNullOrEmpty(registration.Fingerprint))
        {
            await WriteResponseAsync(stream, 400, "{\"message\":\"Request body malformed\"}").ConfigureAwait(false);
            return;
        }

        var authenticatedFingerprint = peerFingerprint ?? registration.Fingerprint;
        var fingerprintMatchesCertificate = peerFingerprint == null ||
            string.Equals(registration.Fingerprint, peerFingerprint, StringComparison.OrdinalIgnoreCase);
        if (fingerprintMatchesCertificate && remoteEp is IPEndPoint ep)
        {
            registration.Fingerprint = authenticatedFingerprint;
            server.InvokeDeviceRegistered(LocalSendProtocolMapper.ToDevice(
                registration, FormatIpAddress(ep.Address), server.DeviceInfo.Port, server.DeviceInfo.Protocol));
        }

        await WriteResponseAsync(stream, 200, System.Text.Json.JsonSerializer.Serialize(LocalSendProtocolMapper.CreateInfo(server.DeviceInfo))).ConfigureAwait(false);
    }

    internal static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
                Logger.Log($"[LocalSendServer] Cleaned up partial/canceled file: {path}", LogLevel.Debug);
            }
        }
        catch (Exception deleteEx)
        {
            Logger.Log($"[LocalSendServer] Failed to delete partial file {path}: {deleteEx.Message}", LogLevel.Warn);
        }
    }

    public static string GetLocalDeviceHashtag()
    {
        try
        {
            var addresses = NetworkInterface.GetAllNetworkInterfaces()
                .SelectMany(networkInterface => networkInterface.GetIPProperties().UnicastAddresses)
                .Select(unicastAddress => unicastAddress.Address);
            return FormatDeviceHashtag(addresses);
        }
        catch { }

        return "#42";
    }

    internal static string FormatDeviceHashtag(IEnumerable<IPAddress> addresses)
    {
        var tags = addresses
            .Where(address => address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address) && !IsIpv4LinkLocal(address))
            .Select(address => address.ToString())
            .OrderByDescending(address => !address.EndsWith(".1", StringComparison.Ordinal))
            .Select(address => address[(address.LastIndexOf('.') + 1)..])
            .Distinct()
            .Take(3)
            .ToList();
        return tags.Count == 0 ? "#42" : "#" + string.Join(" / #", tags);
    }

    private static bool IsIpv4LinkLocal(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return bytes[0] == 169 && bytes[1] == 254;
    }

    public static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024.0):F1} MB";
        return $"{bytes / (1024.0 * 1024.0 * 1024.0):F1} GB";
    }

    internal static string? ResolveTargetPath(string downloadDir, string rawFileName) =>
        LocalSendPathSanitizer.Resolve(downloadDir, rawFileName);

    internal static bool CheckPin(
        string? configuredPin,
        System.Collections.Concurrent.ConcurrentDictionary<string, int> pinAttempts,
        string clientIp,
        string? requestPin,
        out int statusCode,
        out string? jsonResponseBody,
        System.Collections.Concurrent.ConcurrentDictionary<string, DateTime>? attemptTimes = null)
        => LocalSendPinValidator.CheckPin(configuredPin, pinAttempts, clientIp, requestPin, out statusCode, out jsonResponseBody, attemptTimes);
}
