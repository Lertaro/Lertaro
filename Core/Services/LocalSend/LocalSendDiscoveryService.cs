using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Lertaro.Core.Services.LocalSend.Models;

namespace Lertaro.Core.Services.LocalSend;

public sealed class LocalSendDiscoveryService : IDisposable
{
    public const string MulticastGroupIp = "224.0.0.167";
    public const string MulticastGroupIpV6 = "ff12::fd3a:e420";
    public const int DefaultPort = 53317;

    private readonly ConcurrentDictionary<string, LocalSendDeviceInfo> _discoveredDevices = new(StringComparer.OrdinalIgnoreCase);
    private UdpClient? _udpListener;
    private readonly List<UdpClient> _udpListenersV6 = [];
    private CancellationTokenSource? _cts;
    private Task? _listenTask;
    private int _isDiscovering;
    private int _isValidating;

    public event EventHandler<LocalSendDeviceInfo>? DeviceDiscovered;
    public event EventHandler? DeviceListChanged;

    public LocalSendDeviceInfo LocalInfo { get; set; } = new()
    {
        Alias = Environment.MachineName,
        DeviceModel = "Windows",
        DeviceType = "desktop",
        Port = DefaultPort,
        Protocol = "http"
    };

    public int DiscoveryTimeout { get; set; } = 500;

    public IReadOnlyCollection<LocalSendDeviceInfo> DiscoveredDevices => _discoveredDevices.Values.ToList().AsReadOnly();

    public void Start(int port = DefaultPort)
    {
        if (_udpListener != null)
            return;

        _cts = new CancellationTokenSource();
        LocalInfo.Port = port;

        try
        {
            _udpListener = new UdpClient();
            _udpListener.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _udpListener.Client.Bind(new IPEndPoint(IPAddress.Any, port));

            var multicastIp = IPAddress.Parse(MulticastGroupIp);
            foreach (var ip in LocalSendSubnetScanner.GetLocalIPv4Addresses())
            {
                try { _udpListener.JoinMulticastGroup(multicastIp, ip); } catch { }
            }

            _udpListener.MulticastLoopback = true;

            Logger.Log($"[LocalSendDiscovery] Started discovery service on port {port}. Alias={LocalInfo.Alias}, Fingerprint={LocalInfo.Fingerprint}");

            _listenTask = Task.Run(() => ListenLoopAsync(_udpListener, _cts.Token));

            foreach (var listener in LocalSendIpv6Multicast.CreateSockets(port))
            {
                _udpListenersV6.Add(listener);
                _ = Task.Run(() => ListenLoopAsync(listener, _cts.Token));
            }

            _ = Task.Run(AnnounceBurstAsync);
        }
        catch
        {
            Stop();
        }
    }

    public async Task AnnounceAsync()
    {
        try
        {
            var json = JsonSerializer.Serialize(LocalSendProtocolMapper.CreateMulticast(LocalInfo, announcement: true));
            var bytes = Encoding.UTF8.GetBytes(json);

            var multicastEp = new IPEndPoint(IPAddress.Parse(MulticastGroupIp), LocalInfo.Port);
            var multicastEpV6 = new IPEndPoint(IPAddress.Parse(MulticastGroupIpV6), LocalInfo.Port);
            foreach (var ip in LocalSendSubnetScanner.GetLocalIPv4Addresses())
            {
                try
                {
                    using var client = new UdpClient(new IPEndPoint(ip, 0));
                    client.MulticastLoopback = true;
                    await client.SendAsync(bytes, bytes.Length, multicastEp).ConfigureAwait(false);
                }
                catch { }
            }

            foreach (var listener in _udpListenersV6)
            {
                try { await listener.SendAsync(bytes, bytes.Length, multicastEpV6).ConfigureAwait(false); } catch { }
            }
        }
        catch { }
    }

    private async Task ListenLoopAsync(UdpClient listener, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                var result = await listener.ReceiveAsync(token).ConfigureAwait(false);
                var json = Encoding.UTF8.GetString(result.Buffer);
                var multicast = JsonSerializer.Deserialize<LocalSendMulticastDto>(json);
                var device = multicast == null
                    ? null
                    : LocalSendProtocolMapper.ToDevice(
                        multicast, LocalSendServerHelper.FormatIpAddress(result.RemoteEndPoint.Address), LocalInfo.Port, LocalInfo.Protocol);

                if (device != null && !string.IsNullOrEmpty(device.Alias) && device.Fingerprint != LocalInfo.Fingerprint)
                {
                    device.LastSeen = DateTime.UtcNow;

                    AddDiscoveredDevice(device);

                    if (multicast!.Announcement == true || multicast.Announce == true)
                    {
                        _ = LocalSendAnnouncementResponder.RespondAsync(LocalInfo, device);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                await Task.Delay(1000, token).ConfigureAwait(false);
            }
        }
    }

    public void AddDiscoveredDevice(LocalSendDeviceInfo device)
    {
        if (device == null || string.IsNullOrEmpty(device.Alias) || device.Fingerprint == LocalInfo.Fingerprint)
            return;

        device.IpAddress = LocalSendServerHelper.CleanIpAddress(device.IpAddress);
        var key = device.DiscoveryKey;
        var isNew = !_discoveredDevices.TryGetValue(key, out var existingDev);
        device.LastSeen = DateTime.UtcNow;
        _discoveredDevices[key] = device;

        if (isNew)
        {
            Logger.Log($"[LocalSendDiscovery] Discovered device: {device.Alias} ({device.IpAddress}:{device.Port}, model: {device.DeviceModel})", LogLevel.Debug);
            DeviceDiscovered?.Invoke(this, device);
        }
        DeviceListChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task DiscoverDevicesAsync()
    {
        if (Interlocked.Exchange(ref _isDiscovering, 1) != 0)
            return;

        try
        {
            PruneStaleDevices();
            if (!_discoveredDevices.IsEmpty)
                return;

            _ = AnnounceBurstAsync();
            await Task.Delay(1000).ConfigureAwait(false);
            if (_discoveredDevices.IsEmpty)
                await LocalSendSubnetScanner.ScanSubnetAsync(this, LocalInfo, DiscoveryTimeout).ConfigureAwait(false);
        }
        finally
        {
            Volatile.Write(ref _isDiscovering, 0);
        }
    }

    public async Task ValidateDiscoveredDevicesAsync()
    {
        if (Interlocked.Exchange(ref _isValidating, 1) != 0)
            return;

        try
        {
            var checks = _discoveredDevices.ToArray().Select(ValidateDeviceAsync);
            var results = await Task.WhenAll(checks).ConfigureAwait(false);
            var removedAny = false;

            foreach (var (key, reachable) in results)
            {
                if (!reachable && _discoveredDevices.TryRemove(key, out _))
                    removedAny = true;
            }

            if (removedAny)
                DeviceListChanged?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            Volatile.Write(ref _isValidating, 0);
        }
    }

    private async Task<(string Key, bool Reachable)> ValidateDeviceAsync(KeyValuePair<string, LocalSendDeviceInfo> pair)
    {
        using var client = new LocalSendClient();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(Math.Min(DiscoveryTimeout, 1000)));
        var device = pair.Value;
        var response = await client.GetDeviceInfoAsync(
            device.IpAddress, device.Port, device.Https, timeout.Token, device.Version, LocalInfo.Fingerprint).ConfigureAwait(false);
        return (pair.Key, response != null && response.Fingerprint == device.Fingerprint);
    }

    private async Task AnnounceBurstAsync()
    {
        foreach (var delay in new[] { 100, 500, 2000 })
        {
            await Task.Delay(delay).ConfigureAwait(false);
            await AnnounceAsync().ConfigureAwait(false);
        }
    }

    private void PruneStaleDevices()
    {
        var cutoff = DateTime.UtcNow.AddSeconds(-60);
        var removedAny = false;

        foreach (var kvp in _discoveredDevices)
        {
            if (kvp.Value.LastSeen < cutoff)
            {
                if (_discoveredDevices.TryRemove(kvp.Key, out _))
                    removedAny = true;
            }
        }

        if (removedAny)
            DeviceListChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Stop()
    {
        _cts?.Cancel();

        _udpListener?.Close();
        _udpListener?.Dispose();
        _udpListener = null;

        foreach (var listener in _udpListenersV6)
        {
            listener.Close();
            listener.Dispose();
        }
        _udpListenersV6.Clear();

        _discoveredDevices.Clear();
    }

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
    }
}
