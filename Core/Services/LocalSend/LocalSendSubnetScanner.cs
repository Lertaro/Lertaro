using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Lertaro.Core.Services.LocalSend.Models;

namespace Lertaro.Core.Services.LocalSend;

/// <summary>
/// Split out purely to keep LocalSendDiscoveryService.cs under the repo's per-file 300 line limit;
/// provides HTTP TCP subnet scanning fallback when UDP multicast is blocked by routers/firewalls.
/// </summary>
internal static class LocalSendSubnetScanner
{
    internal const int MaxInterfaces = 3;
    internal const int MaxConcurrentProbesPerInterface = 50;
    public static async Task ScanSubnetAsync(LocalSendDiscoveryService discovery, LocalSendDeviceInfo localInfo, int timeoutMs = 500)
    {
        var localIps = SelectInterfaces(GetLocalIPv4Addresses());
        using var identity = LocalSendCertificate.LoadOrCreate();
        using var client = LocalSendHttpClientFactory.Create(identity);
        await Task.WhenAll(localIps.Select(localIp => ScanInterfaceAsync(
            client, discovery, localInfo, localIp, timeoutMs))).ConfigureAwait(false);
    }

    private static Task ScanInterfaceAsync(HttpClient client, LocalSendDiscoveryService discovery,
        LocalSendDeviceInfo localInfo, IPAddress localIp, int timeoutMs) =>
        Parallel.ForEachAsync(
            BuildProbeAddresses(localIp),
            new ParallelOptions { MaxDegreeOfParallelism = MaxConcurrentProbesPerInterface },
            async (ip, _) => await ProbeHostAsync(client, discovery, localInfo, ip, localInfo.Port, timeoutMs).ConfigureAwait(false));

    private static async Task ProbeHostAsync(HttpClient client, LocalSendDiscoveryService discovery,
        LocalSendDeviceInfo localInfo, string ip, int port, int timeoutMs)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(timeoutMs));
            var payload = JsonSerializer.Serialize(LocalSendProtocolMapper.CreateRegister(localInfo));
            using var request = new HttpRequestMessage(HttpMethod.Post, BuildRegistrationUri(localInfo, ip, port))
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
            using var response = await client.SendAsync(request, timeout.Token).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
                var info = JsonSerializer.Deserialize<LocalSendInfoDto>(json);
                if (localInfo.Https && info != null && LocalSendHttpClientFactory.TryGetPeerFingerprint(request, out var fingerprint))
                    info.Fingerprint = fingerprint;
                var device = info == null ? null : LocalSendProtocolMapper.ToDevice(
                    info, LocalSendServerHelper.CleanIpAddress(ip), port, localInfo.Protocol);
                if (device != null && !string.IsNullOrEmpty(device.Alias) && device.Fingerprint != localInfo.Fingerprint)
                    discovery.AddDiscoveredDevice(device);
            }
        }
        catch { }
    }

    public static List<IPAddress> GetLocalIPv4Addresses()
    {
        var list = new List<IPAddress>();
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    continue;
                foreach (var ip in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ip.Address.AddressFamily == AddressFamily.InterNetwork)
                        list.Add(ip.Address);
                }
            }
        }
        catch { }
        return list;
    }

    internal static IReadOnlyList<IPAddress> SelectInterfaces(IEnumerable<IPAddress> localIps) => localIps.Take(MaxInterfaces).ToArray();

    internal static Uri BuildRegistrationUri(LocalSendDeviceInfo localInfo, string ip, int port) =>
        LocalSendApiRoute.BuildUri(ip, port, localInfo.Https, "register", "2.2");

    internal static IEnumerable<string> BuildProbeAddresses(IPAddress localIp)
    {
        var bytes = localIp.GetAddressBytes();
        for (var i = 1; i <= 254; i++)
        {
            if (i != bytes[3])
                yield return $"{bytes[0]}.{bytes[1]}.{bytes[2]}.{i}";
        }
    }
}
