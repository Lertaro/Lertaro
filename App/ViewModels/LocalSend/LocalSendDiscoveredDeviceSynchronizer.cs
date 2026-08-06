using System.Collections.ObjectModel;
using Lertaro.Core.Services.LocalSend.Models;

namespace Lertaro.App.ViewModels.LocalSend;

/// <summary>Synchronizes the send-window list using LocalSend's endpoint and device identity.</summary>
internal static class LocalSendDiscoveredDeviceSynchronizer
{
    internal static void Synchronize(ObservableCollection<LocalSendSendDeviceItem> items, IEnumerable<LocalSendDeviceInfo> devices)
    {
        var deviceList = devices.ToList();
        var keys = deviceList.Select(device => device.DiscoveryKey).ToHashSet();
        for (var index = items.Count - 1; index >= 0; index--)
        {
            if (!keys.Contains(items[index].Device.DiscoveryKey)) items.RemoveAt(index);
        }

        foreach (var device in deviceList)
        {
            var existing = items.FirstOrDefault(item => item.Device.DiscoveryKey == device.DiscoveryKey);
            if (existing == null) items.Add(new LocalSendSendDeviceItem(device));
            else existing.UpdateDevice(device);
        }
    }
}
