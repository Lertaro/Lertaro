using System.ComponentModel;
using System.Runtime.CompilerServices;
using Lertaro.Core.Services.LocalSend.Models;

namespace Lertaro.App.ViewModels.LocalSend;

/// <summary>
/// Wrapper for discovered LocalSend device with per-device PIN input.
/// ponytail: Split out to keep LocalSendSendViewModel.cs under 300 lines limit.
/// </summary>
public sealed class LocalSendSendDeviceItem : INotifyPropertyChanged
{
    private string _pin = string.Empty;
    private bool _isSelected;

    public LocalSendSendDeviceItem(LocalSendDeviceInfo device) => Device = device;

    public LocalSendDeviceInfo Device { get; private set; }

    public string Alias => Device.Alias;
    public string IpAddress => Device.IpAddress;
    public string? DeviceModel => Device.DeviceModel;
    public bool UsesHttps => Device.Https;

    // #xxx tag: last octet of the device's IP, matching LocalSend protocol convention and the settings display.
    public string FingerprintTag
    {
        get
        {
            var ip = Device.IpAddress;
            if (!string.IsNullOrEmpty(ip))
            {
                var lastDot = ip.LastIndexOf('.');
                if (lastDot > 0 && lastDot < ip.Length - 1)
                    return ip[(lastDot + 1)..];
            }
            // Fallback to fingerprint hash if no IP available.
            return (Math.Abs(Device.Fingerprint.GetHashCode()) % 10000).ToString("D4");
        }
    }

    // Icon geometry for the device type per LocalSend v2 spec values: mobile, desktop, web, headless, server.
    // Returns Geometry directly so WPF Path.Data binding works without a converter.
    public System.Windows.Media.Geometry DeviceTypeIcon
    {
        get
        {
            var path = (Device.DeviceType ?? string.Empty).ToLowerInvariant() switch
            {
                "mobile"   => "M17 1.01L7 1c-1.1 0-2 .9-2 2v18c0 1.1.9 2 2 2h10c1.1 0 2-.9 2-2V3c0-1.1-.9-1.99-2-1.99zM17 19H7V5h10v14z",
                "web"      => "M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm-1 17.93c-3.95-.49-7-3.85-7-7.93 0-.62.08-1.21.21-1.79L9 15v1c0 1.1.9 2 2 2v1.93zm6.9-2.54c-.26-.81-1-1.39-1.9-1.39h-1v-3c0-.55-.45-1-1-1H8v-2h2c.55 0 1-.45 1-1V7h2c1.1 0 2-.9 2-2v-.41c2.93 1.19 5 4.06 5 7.41 0 2.08-.8 3.97-2.1 5.39z",
                "headless" or "server" => "M20 13H4c-.55 0-1 .45-1 1v6c0 .55.45 1 1 1h16c.55 0 1-.45 1-1v-6c0-.55-.45-1-1-1zM7 19c-1.1 0-2-.9-2-2s.9-2 2-2 2 .9 2 2-.9 2-2 2zM20 3H4c-.55 0-1 .45-1 1v6c0 .55.45 1 1 1h16c.55 0 1-.45 1-1V4c0-.55-.45-1-1-1zM7 9c-1.1 0-2-.9-2-2s.9-2 2-2 2 .9 2 2-.9 2-2 2z",
                _          => "M20 18c1.1 0 1.99-.9 1.99-2L22 6c0-1.1-.9-2-2-2H4c-1.1 0-2 .9-2 2v10c0 1.1.9 2 2 2H0v2h24v-2h-4zM4 6h16v10H4V6z"
            };
            return System.Windows.Media.Geometry.Parse(path);
        }
    }

    public void UpdateDevice(LocalSendDeviceInfo newDev)
    {
        Device = newDev;
        OnPropertyChanged(nameof(Alias));
        OnPropertyChanged(nameof(IpAddress));
        OnPropertyChanged(nameof(DeviceModel));
        OnPropertyChanged(nameof(UsesHttps));
        OnPropertyChanged(nameof(DeviceTypeIcon));
    }

    public bool IsSelected
    {
        get => _isSelected;
        set { if (_isSelected != value) { _isSelected = value; OnPropertyChanged(); } }
    }

    public string Pin
    {
        get => _pin;
        set { if (_pin != value) { _pin = value; OnPropertyChanged(); } }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
}
