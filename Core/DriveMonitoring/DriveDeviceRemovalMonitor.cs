using Microsoft.Win32.SafeHandles;

namespace Lertaro.Core.DriveMonitoring;

// Registers PnP removal notifications for one volume. The target handle must stay open until the
// query-remove callback; otherwise Configuration Manager has no live device handle to notify.
internal sealed class DriveDeviceRemovalMonitor : IDisposable
{
    private readonly string _drive;
    private readonly Action _onRemovalRequested;
    private readonly Action _onRemovalFailed;
    private readonly Action _onRemovalComplete;
    private readonly DeviceNotificationNative.Callback _callback;
    private readonly object _gate = new();
    private SafeFileHandle? _targetHandle;
    private IntPtr _notification;
    private int _callbackDepth;
    private int _disposed;
    private int _removalRequested;

    private DriveDeviceRemovalMonitor(string drive, Action onRemovalRequested, Action onRemovalFailed, Action onRemovalComplete)
    {
        _drive = drive;
        _onRemovalRequested = onRemovalRequested;
        _onRemovalFailed = onRemovalFailed;
        _onRemovalComplete = onRemovalComplete;
        _callback = HandleNotification;
    }

    public static DriveDeviceRemovalMonitor? Register(
        string drive,
        Action onRemovalRequested,
        Action onRemovalFailed,
        Action onRemovalComplete)
    {
        var monitor = new DriveDeviceRemovalMonitor(drive, onRemovalRequested, onRemovalFailed, onRemovalComplete);
        if (!monitor.RegisterCore(drive))
        {
            monitor.Dispose();
            return null;
        }
        return monitor;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        if (Volatile.Read(ref _callbackDepth) != 0)
        {
            _ = Task.Run(UnregisterCore);
            return;
        }
        UnregisterCore();
    }

    private bool RegisterCore(string drive)
    {
        var handle = Win32Api.CreateFileW(
            $"\\\\.\\{drive}:",
            Win32Api.GENERIC_READ,
            Win32Api.FILE_SHARE_READ | Win32Api.FILE_SHARE_WRITE,
            IntPtr.Zero,
            Win32Api.OPEN_EXISTING,
            0,
            IntPtr.Zero);
        if (handle.IsInvalid)
        {
            handle.Dispose();
            Logger.Log($"[DeviceNotification] Failed to open {drive}: for removal notifications.", LogLevel.Warn);
            return false;
        }

        var filter = new DeviceNotificationNative.Filter
        {
            Size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<DeviceNotificationNative.Filter>(),
            FilterType = DeviceNotificationNative.DeviceHandleFilter,
            UnionData = new byte[400]
        };
        BitConverter.GetBytes(handle.DangerousGetHandle().ToInt64()).CopyTo(filter.UnionData, 0);
        var result = DeviceNotificationNative.CM_Register_Notification(ref filter, IntPtr.Zero, _callback, out _notification);
        if (result != DeviceNotificationNative.Success)
        {
            handle.Dispose();
            Logger.Log($"[DeviceNotification] Failed to register removal notifications for {drive}: {result}.", LogLevel.Warn);
            return false;
        }
        _targetHandle = handle;
        return true;
    }

    private uint HandleNotification(IntPtr notification, IntPtr context, int action, IntPtr eventData, uint eventDataSize)
    {
        Interlocked.Increment(ref _callbackDepth);
        try
        {
            if (action is DeviceNotificationNative.DeviceQueryRemove or DeviceNotificationNative.DeviceRemovePending)
            {
                RequestRemoval();
            }
            else if (action == DeviceNotificationNative.DeviceQueryRemoveFailed)
            {
                RestoreAfterFailedRemoval();
            }
            else if (action == DeviceNotificationNative.DeviceRemoveComplete)
            {
                CloseTargetHandle();
                try { _onRemovalComplete(); }
                catch (Exception ex) { Logger.Log($"[DeviceNotification] Failed to handle completed drive removal: {ex.Message}", LogLevel.Error); }
                _ = Task.Run(UnregisterCore);
            }
            else if (action == DeviceNotificationNative.DeviceCustomEvent &&
                     DeviceNotificationNative.TryReadCustomEventGuid(eventData, eventDataSize, out var eventGuid))
            {
                if (eventGuid == DeviceNotificationNative.VolumeLockEvent || eventGuid == DeviceNotificationNative.VolumeDismountEvent)
                    RequestRemoval();
                else if (eventGuid == DeviceNotificationNative.VolumeLockFailedEvent || eventGuid == DeviceNotificationNative.VolumeDismountFailedEvent)
                    RestoreAfterFailedRemoval();
            }
            return DeviceNotificationNative.Success;
        }
        finally
        {
            Interlocked.Decrement(ref _callbackDepth);
        }
    }

    private void UnregisterCore()
    {
        lock (_gate)
        {
            var notification = Interlocked.Exchange(ref _notification, IntPtr.Zero);
            if (notification != IntPtr.Zero)
                DeviceNotificationNative.CM_Unregister_Notification(notification);
            CloseTargetHandle();
        }
    }

    // Puts the notification back after a removal attempt was refused, because answering one closed the very
    // handle the registration reports on: an unarmed monitor never hears the next attempt, so a single eject
    // vetoed by another program would leave the volume un-ejectable until something else rebuilt its monitor.
    // Off the callback thread, and serialized with Dispose: CM_Unregister_Notification for a notification that
    // is mid-callback is the same hazard Dispose() already defers for.
    private void Rearm() => _ = Task.Run(() =>
    {
        lock (_gate)
        {
            // Skip when already armed (another refusal re-armed first), when this volume is gone for good,
            // and while a removal request is being answered -- re-arming inside that window would put a handle
            // back on the volume the system is trying to lock, and the next refusal would bring us back here.
            if (Volatile.Read(ref _disposed) != 0 || _targetHandle != null || Volatile.Read(ref _removalRequested) != 0)
                return;

            var notification = Interlocked.Exchange(ref _notification, IntPtr.Zero);
            if (notification != IntPtr.Zero)
                DeviceNotificationNative.CM_Unregister_Notification(notification);

            RegisterCore(_drive);
        }
    });

    private void CloseTargetHandle() => Interlocked.Exchange(ref _targetHandle, null)?.Dispose();

    private void RequestRemoval()
    {
        CloseTargetHandle();
        if (Interlocked.Exchange(ref _removalRequested, 1) != 0)
            return;

        try { _onRemovalRequested(); }
        catch (Exception ex) { Logger.Log($"[DeviceNotification] Failed to release a drive monitor: {ex.Message}", LogLevel.Error); }
    }

    private void RestoreAfterFailedRemoval()
    {
        Interlocked.Exchange(ref _removalRequested, 0);
        try { _onRemovalFailed(); }
        catch (Exception ex) { Logger.Log($"[DeviceNotification] Failed to restore a drive monitor: {ex.Message}", LogLevel.Error); }
        Rearm();
    }
}
