using System.Runtime.InteropServices;

namespace Lertaro.Core.DriveMonitoring;

internal static class DeviceNotificationNative
{
    internal const uint Success = 0;
    internal const int DeviceHandleFilter = 1;
    internal const int DeviceQueryRemove = 2;
    internal const int DeviceQueryRemoveFailed = 3;
    internal const int DeviceRemovePending = 4;
    internal const int DeviceRemoveComplete = 5;
    internal const int DeviceCustomEvent = 6;

    internal static readonly Guid VolumeLockEvent = new("50708874-c9af-11d1-8fef-00a0c9a06d32");
    internal static readonly Guid VolumeLockFailedEvent = new("ae2eed10-0ba8-11d2-8ffb-00a0c9a06d32");
    internal static readonly Guid VolumeDismountEvent = new("d16a55e8-1059-11d2-8ffd-00a0c9a06d32");
    internal static readonly Guid VolumeDismountFailedEvent = new("e3c5b178-105d-11d2-8ffd-00a0c9a06d32");

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    internal delegate uint Callback(IntPtr notification, IntPtr context, int action, IntPtr eventData, uint eventDataSize);

    // CM_NOTIFY_FILTER contains a union whose largest member is the 200-character device-instance
    // buffer. The native API validates cbSize against the complete structure, even when the device
    // handle variant is selected; omitting the union tail makes CM_Register_Notification return
    // CR_INVALID_POINTER (31) before any removal callback can be delivered.
    [StructLayout(LayoutKind.Sequential)]
    internal struct Filter
    {
        public uint Size;
        public uint Flags;
        public int FilterType;
        public uint Reserved;

        // MAX_DEVICE_ID_LEN is 200 UTF-16 characters, so the union occupies 400 bytes on the
        // supported 64-bit Windows targets. Only the first bytes are used for hTarget here.
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 400)]
        public byte[] UnionData;
    }

    internal static bool TryReadCustomEventGuid(IntPtr eventData, uint eventDataSize, out Guid eventGuid)
    {
        eventGuid = Guid.Empty;
        if (eventData == IntPtr.Zero || eventDataSize < 24)
            return false;

        eventGuid = Marshal.PtrToStructure<Guid>(IntPtr.Add(eventData, 8));
        return true;
    }

    [DllImport("CfgMgr32.dll", CallingConvention = CallingConvention.Winapi)]
    internal static extern uint CM_Register_Notification(
        ref Filter filter,
        IntPtr context,
        Callback callback,
        out IntPtr notification);

    [DllImport("CfgMgr32.dll", CallingConvention = CallingConvention.Winapi)]
    internal static extern uint CM_Unregister_Notification(IntPtr notification);
}
