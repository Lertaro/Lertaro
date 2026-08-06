using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Lertaro.Core.Services.LocalSend;

/// <summary>Creates one IPv6 multicast socket per active network interface, matching LocalSend discovery behavior.</summary>
internal static class LocalSendIpv6Multicast
{
    private static readonly IPAddress Group = IPAddress.Parse(LocalSendDiscoveryService.MulticastGroupIpV6);

    internal static IReadOnlyList<UdpClient> CreateSockets(int port)
    {
        var sockets = new List<UdpClient>();
        foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (networkInterface.OperationalStatus != OperationalStatus.Up || networkInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                continue;

            try
            {
                var ipv6 = networkInterface.GetIPProperties().GetIPv6Properties();
                foreach (var address in networkInterface.GetIPProperties().UnicastAddresses.Where(item =>
                    item.Address.AddressFamily == AddressFamily.InterNetworkV6 && !IPAddress.IPv6Loopback.Equals(item.Address)))
                {
                    var client = new UdpClient(AddressFamily.InterNetworkV6);
                    try
                    {
                        client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                        client.Client.Bind(new IPEndPoint(address.Address, port));
                        client.Client.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.MulticastInterface, ipv6.Index);
                        client.JoinMulticastGroup(Group, ipv6.Index);
                        client.MulticastLoopback = true;
                        sockets.Add(client);
                    }
                    catch
                    {
                        client.Dispose();
                    }
                }
            }
            catch { }
        }

        return sockets;
    }

    internal static IPEndPoint CreateEndpoint(int port) => new(Group, port);
}
