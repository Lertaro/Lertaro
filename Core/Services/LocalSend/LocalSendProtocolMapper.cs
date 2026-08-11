using Lertaro.Core.Services.LocalSend.Models;

namespace Lertaro.Core.Services.LocalSend;

/// <summary>Maps the UI device model to the smaller, endpoint-specific LocalSend wire DTOs.</summary>
internal static class LocalSendProtocolMapper
{
    internal static LocalSendInfoDto CreateInfo(LocalSendDeviceInfo device) => new()
    {
        Alias = device.Alias, Version = device.Version, DeviceModel = device.DeviceModel,
        DeviceType = device.DeviceType, Fingerprint = device.Fingerprint, Download = device.Download
    };

    internal static LocalSendRegisterDto CreateRegister(LocalSendDeviceInfo device) => new()
    {
        Alias = device.Alias, Version = device.Version, DeviceModel = device.DeviceModel,
        DeviceType = device.DeviceType, Fingerprint = device.Fingerprint, Port = device.Port,
        Protocol = device.Protocol, Download = device.Download
    };

    internal static LocalSendMulticastDto CreateMulticast(LocalSendDeviceInfo device, bool announcement) => new()
    {
        Alias = device.Alias, Version = device.Version, DeviceModel = device.DeviceModel,
        DeviceType = device.DeviceType, Fingerprint = device.Fingerprint, Port = device.Port,
        Protocol = device.Protocol, Download = device.Download,
        Announcement = IsV22OrNewer(device.Version) ? null : announcement,
        Announce = announcement
    };

    internal static bool IsAnnouncement(LocalSendMulticastDto dto) =>
        dto.Announcement == true || dto.Announce == true ||
        dto.Announcement == null && dto.Announce == null && IsV22OrNewer(dto.Version);

    internal static LocalSendInfoRegisterDto CreateInfoRegister(LocalSendDeviceInfo device) => new()
    {
        Alias = device.Alias, Version = device.Version, DeviceModel = device.DeviceModel,
        DeviceType = device.DeviceType, Fingerprint = device.Fingerprint, Port = device.Port,
        Protocol = device.Protocol, Download = device.Download
    };

    internal static LocalSendDeviceInfo ToDevice(LocalSendInfoDto dto, string ipAddress, int port, string protocol) => new()
    {
        Alias = dto.Alias, DeviceModel = dto.DeviceModel, DeviceType = ResolveDeviceType(dto.DeviceType),
        Fingerprint = dto.Fingerprint ?? string.Empty, Version = dto.Version ?? "1.0", Download = dto.Download ?? false,
        IpAddress = ipAddress, Port = port, Protocol = protocol
    };

    internal static LocalSendDeviceInfo ToDevice(LocalSendRegisterDto dto, string ipAddress, int fallbackPort, string fallbackProtocol) => new()
    {
        Alias = dto.Alias, Version = dto.Version ?? "1.0", DeviceModel = dto.DeviceModel, DeviceType = ResolveDeviceType(dto.DeviceType),
        Fingerprint = dto.Fingerprint, Download = dto.Download ?? false, IpAddress = ipAddress,
        Port = dto.Port ?? fallbackPort, Protocol = ResolveProtocol(dto.Protocol, fallbackProtocol)
    };

    internal static LocalSendDeviceInfo ToDevice(LocalSendMulticastDto dto, string ipAddress, int fallbackPort, string fallbackProtocol) => new()
    {
        Alias = dto.Alias, Version = dto.Version ?? "1.0", DeviceModel = dto.DeviceModel, DeviceType = ResolveDeviceType(dto.DeviceType),
        Fingerprint = dto.Fingerprint, Download = dto.Download ?? false, IpAddress = ipAddress,
        Port = dto.Port ?? fallbackPort, Protocol = ResolveProtocol(dto.Protocol, fallbackProtocol), Announcement = dto.Announcement
    };

    internal static LocalSendDeviceInfo ToDevice(LocalSendInfoRegisterDto dto, string ipAddress, int fallbackPort, string fallbackProtocol) => new()
    {
        Alias = dto.Alias, Version = dto.Version ?? "1.0", DeviceModel = dto.DeviceModel, DeviceType = ResolveDeviceType(dto.DeviceType),
        Fingerprint = dto.Fingerprint ?? string.Empty, IpAddress = ipAddress, Port = dto.Port ?? fallbackPort,
        Protocol = ResolveProtocol(dto.Protocol, fallbackProtocol), Download = dto.Download ?? false
    };

    private static string ResolveProtocol(string? protocol, string fallbackProtocol) => protocol == null
        ? fallbackProtocol
        : string.Equals(protocol, "http", StringComparison.OrdinalIgnoreCase) ? "http" : "https";

    private static string ResolveDeviceType(string? deviceType) => deviceType is "mobile" or "desktop" or "web" or "headless" or "server"
        ? deviceType
        : "desktop";

    private static bool IsV22OrNewer(string? version) =>
        Version.TryParse(version, out var parsed) && parsed >= new Version(2, 2);
}
