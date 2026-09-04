using System.Runtime.InteropServices;

namespace Lertaro.Plugins.AudioDeviceSelector.CoreAudio;

internal sealed class CoreAudioDeviceProvider
{
    internal IReadOnlyList<AudioDeviceInfo> GetActiveDevices()
    {
        var enumerator = (IAudioDeviceEnumerator)new AudioDeviceEnumeratorClass();
        try
        {
            return
            [
                .. GetDevices(enumerator, DataFlow.Render, AudioDeviceDirection.Output),
                .. GetDevices(enumerator, DataFlow.Capture, AudioDeviceDirection.Input)
            ];
        }
        finally
        {
            NativeMethods.Release(enumerator);
        }
    }

    private static IReadOnlyList<AudioDeviceInfo> GetDevices(
        IAudioDeviceEnumerator enumerator,
        DataFlow dataFlow,
        AudioDeviceDirection direction)
    {
        IAudioDeviceCollection? collection = null;
        try
        {
            var defaultDeviceId = GetDefaultDeviceId(enumerator, dataFlow);
            NativeMethods.ThrowIfFailed(enumerator.EnumAudioEndpoints(dataFlow, DeviceState.Active, out collection));
            NativeMethods.ThrowIfFailed(collection.GetCount(out var count));

            var devices = new List<AudioDeviceInfo>((int)count);
            for (uint index = 0; index < count; index++)
            {
                IAudioDevice? device = null;
                try
                {
                    NativeMethods.ThrowIfFailed(collection.Item(index, out device));
                    var info = ReadDevice(device, direction, defaultDeviceId);
                    if (info != null)
                        devices.Add(info);
                }
                finally
                {
                    NativeMethods.Release(device);
                }
            }

            return devices;
        }
        finally
        {
            NativeMethods.Release(collection);
        }
    }

    internal void SetDefaultDevice(string deviceId)
    {
        var policyClient = (IPolicyConfig)new PolicyConfigClientClass();
        try
        {
            NativeMethods.ThrowIfFailed(policyClient.SetDefaultEndpoint(deviceId, ERole.Multimedia));
        }
        finally
        {
            NativeMethods.Release(policyClient);
        }
    }

    private static string? GetDefaultDeviceId(IAudioDeviceEnumerator enumerator, DataFlow dataFlow)
    {
        IAudioDevice? device = null;
        var idPointer = IntPtr.Zero;
        try
        {
            if (enumerator.GetDefaultAudioEndpoint(dataFlow, ERole.Multimedia, out device) < 0)
                return null;

            NativeMethods.ThrowIfFailed(device.GetId(out idPointer));
            return Marshal.PtrToStringUni(idPointer);
        }
        finally
        {
            NativeMethods.Release(device);
            if (idPointer != IntPtr.Zero)
                Marshal.FreeCoTaskMem(idPointer);
        }
    }

    private static AudioDeviceInfo? ReadDevice(
        IAudioDevice device,
        AudioDeviceDirection direction,
        string? defaultDeviceId)
    {
        NativeMethods.ThrowIfFailed(device.GetId(out var idPointer));
        IPropertyStore? propertyStore = null;
        try
        {
            var id = Marshal.PtrToStringUni(idPointer);
            if (string.IsNullOrWhiteSpace(id))
                return null;

            NativeMethods.ThrowIfFailed(device.OpenPropertyStore(StorageAccessMode.Read, out propertyStore));
            var friendlyName = PropertyStoreReader.ReadString(propertyStore, PropertyKeys.DeviceFriendlyName);
            return string.IsNullOrWhiteSpace(friendlyName)
                ? null
                : new AudioDeviceInfo(
                    id,
                    friendlyName,
                    direction,
                    string.Equals(id, defaultDeviceId, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            NativeMethods.Release(propertyStore);
            if (idPointer != IntPtr.Zero)
                Marshal.FreeCoTaskMem(idPointer);
        }
    }
}
