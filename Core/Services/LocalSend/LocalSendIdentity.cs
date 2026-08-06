using Lertaro.Core.Services.LocalSend.Models;

namespace Lertaro.Core.Services.LocalSend;

/// <summary>
/// Creates the one stable protocol identity shared by the LocalSend HTTP server and discovery service.
/// </summary>
internal static class LocalSendIdentity
{
    internal static string EnsureFingerprint(LocalSendSettingsModel settings)
    {
        if (string.IsNullOrWhiteSpace(settings.DeviceFingerprint))
            settings.DeviceFingerprint = Guid.NewGuid().ToString("N");

        return settings.DeviceFingerprint;
    }

    internal static LocalSendDeviceInfo CreateDeviceInfo(LocalSendSettingsModel settings, string alias) => new()
    {
        Alias = alias,
        Fingerprint = EnsureFingerprint(settings),
        Port = settings.Port > 0 ? settings.Port : LocalSendDiscoveryService.DefaultPort,
        Protocol = settings.EnableHttps ? "https" : "http"
    };
}
