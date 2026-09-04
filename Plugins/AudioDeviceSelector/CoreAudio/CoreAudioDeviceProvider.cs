using System.Runtime.InteropServices;

namespace Lertaro.Plugins.AudioDeviceSelector.CoreAudio;

internal sealed class CoreAudioDeviceProvider
{
    internal IReadOnlyList<AudioDeviceInfo> GetActiveRenderDevices()
    {
        var enumerator = (IAudioDeviceEnumerator)new AudioDeviceEnumeratorClass();
        IAudioDeviceCollection? collection = null;
        try
        {
            NativeMethods.ThrowIfFailed(enumerator.EnumAudioEndpoints(DataFlow.Render, DeviceState.Active, out collection));
            NativeMethods.ThrowIfFailed(collection.GetCount(out var count));

            var devices = new List<AudioDeviceInfo>((int)count);
            for (uint index = 0; index < count; index++)
            {
                IAudioDevice? device = null;
                try
                {
                    NativeMethods.ThrowIfFailed(collection.Item(index, out device));
                    var info = ReadDevice(device);
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
            NativeMethods.Release(enumerator);
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

    private static AudioDeviceInfo? ReadDevice(IAudioDevice device)
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
            return string.IsNullOrWhiteSpace(friendlyName) ? null : new AudioDeviceInfo(id, friendlyName);
        }
        finally
        {
            NativeMethods.Release(propertyStore);
            if (idPointer != IntPtr.Zero)
                Marshal.FreeCoTaskMem(idPointer);
        }
    }
}
