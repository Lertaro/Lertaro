using System.Runtime.InteropServices;

namespace Lertaro.Plugins.AudioDeviceSelector.CoreAudio;

internal enum DataFlow
{
    Render,
    Capture,
    All
}

[Flags]
internal enum DeviceState
{
    Active = 0x1,
    Disabled = 0x2,
    NotPresent = 0x4,
    Unplugged = 0x8,
    All = 0xF
}

internal enum StorageAccessMode
{
    Read = 0
}

[ComImport]
[Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
internal class AudioDeviceEnumeratorClass
{
}

[ComImport]
[Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioDeviceEnumerator
{
    [PreserveSig] int EnumAudioEndpoints(DataFlow dataFlow, DeviceState stateMask, out IAudioDeviceCollection devices);
    [PreserveSig] int GetDefaultAudioEndpoint(DataFlow dataFlow, ERole role, out IAudioDevice device);
    [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IAudioDevice device);
    [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr client);
    [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr client);
}

[ComImport]
[Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioDeviceCollection
{
    [PreserveSig] int GetCount(out uint count);
    [PreserveSig] int Item(uint index, out IAudioDevice device);
}

[ComImport]
[Guid("D666063F-1587-4E43-81F1-B948E807363F")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioDevice
{
    [PreserveSig] int Activate(ref Guid interfaceId, uint clsContext, IntPtr activationParams, out IntPtr interfacePointer);
    [PreserveSig] int OpenPropertyStore(StorageAccessMode accessMode, out IPropertyStore propertyStore);
    [PreserveSig] int GetId(out IntPtr id);
    [PreserveSig] int GetState(out DeviceState state);
}

[ComImport]
[Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPropertyStore
{
    [PreserveSig] int GetCount(out uint count);
    [PreserveSig] int GetAt(uint index, out PropertyKey key);
    [PreserveSig] int GetValue(ref PropertyKey key, IntPtr propertyValue);
    [PreserveSig] int SetValue(ref PropertyKey key, IntPtr propertyValue);
    [PreserveSig] int Commit();
}

[StructLayout(LayoutKind.Sequential)]
internal struct PropertyKey
{
    internal Guid FormatId;
    internal uint PropertyId;
}

internal static class PropertyKeys
{
    internal static readonly PropertyKey DeviceFriendlyName = new()
    {
        FormatId = new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"),
        PropertyId = 14
    };
}

internal static class PropertyStoreReader
{
    private const short VariantTypeString = 31;
    private const short VariantTypeStringObject = 8;

    internal static string? ReadString(IPropertyStore propertyStore, PropertyKey key)
    {
        // PROPVARIANT is 16 bytes on x86 and 24 bytes on x64 because its largest union member
        // contains a pointer-sized array descriptor. Passing an explicitly sized native buffer
        // keeps the property-store call layout correct on both architectures.
        var size = IntPtr.Size == 8 ? 24 : 16;
        var propertyValue = Marshal.AllocCoTaskMem(size);
        try
        {
            for (var offset = 0; offset < size; offset++)
                Marshal.WriteByte(propertyValue, offset, 0);

            NativeMethods.ThrowIfFailed(propertyStore.GetValue(ref key, propertyValue));
            var variantType = Marshal.ReadInt16(propertyValue);
            var valuePointer = Marshal.ReadIntPtr(propertyValue, 8);
            if (valuePointer == IntPtr.Zero)
                return null;

            return variantType switch
            {
                VariantTypeString => Marshal.PtrToStringUni(valuePointer),
                VariantTypeStringObject => Marshal.PtrToStringBSTR(valuePointer),
                _ => null
            };
        }
        finally
        {
            NativeMethods.PropVariantClear(propertyValue);
            Marshal.FreeCoTaskMem(propertyValue);
        }
    }
}

internal static class NativeMethods
{
    [DllImport("ole32.dll", ExactSpelling = true)]
    internal static extern int PropVariantClear(IntPtr propertyValue);

    internal static void ThrowIfFailed(int hResult)
    {
        if (hResult < 0)
            Marshal.ThrowExceptionForHR(hResult);
    }

    internal static void Release(object? comObject)
    {
        if (comObject != null && Marshal.IsComObject(comObject))
            Marshal.ReleaseComObject(comObject);
    }
}
