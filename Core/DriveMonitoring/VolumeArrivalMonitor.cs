using System.Runtime.InteropServices;

namespace Lertaro.Core.DriveMonitoring;

// Process-wide listener for volume device interface arrivals. This is the event side of "a drive came
// back": previously the only way a reinserted drive was noticed was the two-second poll in
// DriveReattachWaiter, which both costs a volume enumeration per drive per tick and, worse, needs a
// waiter to have been armed in the first place. A device interface notification is raised by the volume
// device itself, so it does not depend on the filesystem driver -- an exFAT stick arrives through the
// same interface as an NTFS one.
//
// Registration is best effort: if Configuration Manager refuses it (session isolation, policy), the
// caller's loop keeps its fallback poll and the process behaves exactly as before.
internal static class VolumeArrivalMonitor
{
    private static readonly object Gate = new();
    private static readonly DeviceNotificationNative.Callback Callback = HandleNotification;
    private static IntPtr _notification;
    private static bool _registrationAttempted;

    /// <summary>Raised on a volume device interface arrival, on the Configuration Manager callback thread.</summary>
    /// <remarks>
    /// Handlers must be quick: this thread belongs to the PnP manager. Raising the event directly is
    /// fine for the only subscriber (a waiter that releases a semaphore), and dispatching to the pool
    /// would only add a window in which a second arrival coalesces.
    /// </remarks>
    public static event EventHandler? Arrived;

    /// <summary>True when the PnP manager accepted the registration; false means polling is the only signal.</summary>
    public static bool IsRegistered => Volatile.Read(ref _notification) != IntPtr.Zero;

    /// <summary>Registers once per process; later calls are no-ops.</summary>
    public static void EnsureStarted()
    {
        lock (Gate)
        {
            if (_registrationAttempted)
                return;

            _registrationAttempted = true;
            try
            {
                var filter = new DeviceNotificationNative.Filter
                {
                    Size = (uint)Marshal.SizeOf<DeviceNotificationNative.Filter>(),
                    FilterType = DeviceNotificationNative.DeviceInterfaceFilter,
                    UnionData = new byte[400]
                };
                DeviceNotificationNative.VolumeDeviceInterface.ToByteArray().CopyTo(filter.UnionData, 0);

                var result = DeviceNotificationNative.CM_Register_Notification(ref filter, IntPtr.Zero, Callback, out _notification);
                if (result != DeviceNotificationNative.Success)
                {
                    _notification = IntPtr.Zero;
                    Logger.Log($"[VolumeArrival] Failed to register volume arrival notifications: {result}; falling back to polling.", LogLevel.Warn);
                    return;
                }
                Logger.Log("[VolumeArrival] Volume arrival notifications registered.", LogLevel.Debug);
            }
            catch (Exception ex)
            {
                _notification = IntPtr.Zero;
                Logger.Log($"[VolumeArrival] Failed to register volume arrival notifications: {ex.Message}; falling back to polling.", LogLevel.Warn);
            }
        }
    }

    private static uint HandleNotification(IntPtr notification, IntPtr context, int action, IntPtr eventData, uint eventDataSize)
    {
        if (action != DeviceNotificationNative.DeviceInterfaceArrival)
            return DeviceNotificationNative.Success;

        try
        {
            Arrived?.Invoke(null, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Logger.Log($"[VolumeArrival] Arrival handler failed: {ex.Message}", LogLevel.Error);
        }
        return DeviceNotificationNative.Success;
    }
}
