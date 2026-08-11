using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Lertaro.Core.Services.LocalSend.Models;

namespace Lertaro.Core.Services.LocalSend;

/// <summary>
/// Sends the protocol's direct registration response and its multicast fallback.
/// Split from LocalSendDiscoveryService to keep the discovery implementation under the repository line limit.
/// </summary>
internal static class LocalSendAnnouncementResponder
{
    public static async Task<bool> RespondAsync(LocalSendDeviceInfo localInfo, LocalSendDeviceInfo peer)
    {
        try
        {
            using var identity = LocalSendCertificate.LoadOrCreate();
            using var client = LocalSendHttpClientFactory.Create(
                identity, peer.Https ? peer.Fingerprint : null, TimeSpan.FromSeconds(3));
            var json = JsonSerializer.Serialize(LocalSendProtocolMapper.CreateRegister(localInfo));
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var response = await client.PostAsync(BuildRegistrationUri(peer), content).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"Registration returned {(int)response.StatusCode}.");
            return true;
        }
        catch
        {
            await SendMulticastResponseAsync(localInfo).ConfigureAwait(false);
            return false;
        }
    }

    private static async Task SendMulticastResponseAsync(LocalSendDeviceInfo localInfo)
    {
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(LocalSendProtocolMapper.CreateMulticast(localInfo, announcement: false));
            var endpoint = new IPEndPoint(IPAddress.Parse(LocalSendDiscoveryService.MulticastGroupIp), localInfo.Port);
            foreach (var ip in LocalSendSubnetScanner.GetLocalIPv4Addresses())
            {
                try
                {
                    using var client = new UdpClient(new IPEndPoint(ip, 0));
                    client.JoinMulticastGroup(endpoint.Address, ip);
                    client.MulticastLoopback = true;
                    await client.SendAsync(bytes, bytes.Length, endpoint).ConfigureAwait(false);
                }
                catch { }
            }
        }
        catch { }
    }

    internal static Uri BuildRegistrationUri(LocalSendDeviceInfo peer) => LocalSendApiRoute.BuildUri(peer.IpAddress, peer.Port, peer.Https, "register", peer.Version);
}
