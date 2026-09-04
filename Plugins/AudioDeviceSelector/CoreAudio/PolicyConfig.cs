using System.Runtime.InteropServices;

namespace Lertaro.Plugins.AudioDeviceSelector.CoreAudio;

internal enum ERole
{
    Console,
    Multimedia,
    Communications
}

[ComImport]
[Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")]
internal class PolicyConfigClientClass
{
}

// Windows exposes SetDefaultEndpoint through this policy COM interface rather than the public
// endpoint-enumeration API. The leading methods must stay in the vtable even though this plugin
// only calls SetDefaultEndpoint.
[ComImport]
[Guid("F8679F50-850A-41CF-9C72-430F290290C8")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPolicyConfig
{
    [PreserveSig] int Unused1();
    [PreserveSig] int Unused2();
    [PreserveSig] int Unused3();
    [PreserveSig] int Unused4();
    [PreserveSig] int Unused5();
    [PreserveSig] int Unused6();
    [PreserveSig] int Unused7();
    [PreserveSig] int Unused8();
    [PreserveSig] int GetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string deviceId, ref PropertyKey key, IntPtr propertyValue);
    [PreserveSig] int SetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string deviceId, ref PropertyKey key, IntPtr propertyValue);
    [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, ERole role);
    [PreserveSig] int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string deviceId, [MarshalAs(UnmanagedType.I2)] short isVisible);
}
