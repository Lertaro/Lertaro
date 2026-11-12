using Microsoft.Win32.SafeHandles;

namespace Lertaro.Core.DriveMonitoring;

// Registers PnP removal notifications for a volume handle. USN monitoring supplies its actual read
// handle, so replacing that handle also replaces the notification registration.
internal sealed class DriveDeviceRemovalMonitor : IDisposable
{
    private readonly string _drive;
    private readonly Action<DriveDeviceRemovalMonitor> _onRemovalRequested;
    private readonly Action<DriveDeviceRemovalMonitor> _onRemovalFailed;
    private readonly Action<DriveDeviceRemovalMonitor> _onRemovalComplete;
    private readonly DeviceNotificationNative.Callback _callback;
    private readonly object _gate = new();
    private SafeFileHandle? _targetHandle;
    private IntPtr _notification;
    private int _callbackDepth;
    private bool _disposed;
    private bool _invalidated;
    private bool _removalRequested;

    internal DriveDeviceRemovalMonitor(string drive, Action<DriveDeviceRemovalMonitor> onRemovalRequested,
        Action<DriveDeviceRemovalMonitor> onRemovalFailed, Action<DriveDeviceRemovalMonitor> onRemovalComplete)
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
        Action onRemovalComplete,
        SafeFileHandle? volumeHandle = null)
    {
        var monitor = new DriveDeviceRemovalMonitor(drive, source => source.TryInvoke(onRemovalRequested),
            source => source.TryInvoke(onRemovalFailed), source => source.TryInvoke(onRemovalComplete));
        if (!monitor.RegisterCore(drive, volumeHandle))
        {
            monitor.Dispose();
            return null;
        }
        return monitor;
    }

    internal void Invalidate()
    {
        lock (_gate) _invalidated = true;
    }

    internal bool TryInvoke(Action action)
    {
        lock (_gate)
        {
            if (_invalidated) return false;
            action();
            return true;
        }
    }

    public void Dispose()
    {
        bool defer;
        lock (_gate)
        {
            _invalidated = true;
            if (_disposed) return;
            _disposed = true;
            defer = _callbackDepth != 0;
        }
        if (defer) _ = Task.Run(UnregisterCore);
        else UnregisterCore();
    }

    internal bool RegisterCore(string drive, SafeFileHandle? volumeHandle)
    {
        // The shared SafeFileHandle is idempotently closed by either its I/O owner or removal.
        // Folder scans without a persistent volume handle still need their own notification handle.
        var handle = volumeHandle ?? Win32Api.CreateFileW(
            $"\\\\.\\{drive}:",
            Win32Api.GENERIC_READ,
            Win32Api.FILE_SHARE_READ | Win32Api.FILE_SHARE_WRITE | Win32Api.FILE_SHARE_DELETE,
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
        lock (_gate)
        {
            if (_invalidated)
            {
                handle.Dispose();
                return false;
            }
            _targetHandle = handle;
        }
        var addedReference = false;
        uint result;
        IntPtr notification;
        try
        {
            handle.DangerousAddRef(ref addedReference);
            BitConverter.GetBytes(handle.DangerousGetHandle().ToInt64()).CopyTo(filter.UnionData, 0);
            result = DeviceNotificationNative.CM_Register_Notification(ref filter, IntPtr.Zero, _callback, out notification);
        }
        catch (ObjectDisposedException) when (handle.IsClosed)
        {
            return false;
        }
        finally
        {
            if (addedReference) handle.DangerousRelease();
        }
        if (result != DeviceNotificationNative.Success)
        {
            CloseTargetHandle();
            Logger.Log($"[DeviceNotification] Failed to register removal notifications for {drive}: {result}.", LogLevel.Warn);
            return false;
        }
        lock (_gate)
        {
            _notification = notification;
            if (!_invalidated) return true;
        }
        // Disposal can win while CM_Register_Notification is still returning its handle.
        _ = Task.Run(UnregisterCore);
        return false;
    }

    internal uint HandleNotification(IntPtr notification, IntPtr context, int action, IntPtr eventData, uint eventDataSize)
    {
        lock (_gate)
        {
            if (_invalidated) return DeviceNotificationNative.Success;
            _callbackDepth++;
        }
        try
        {
            Logger.Log($"[DeviceNotification] Drive {_drive}: action={action}.");
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
                if (TryInvoke(CloseTargetHandle))
                {
                    try { _onRemovalComplete(this); }
                    catch (Exception ex) { Logger.Log($"[DeviceNotification] Failed to handle completed drive removal: {ex.Message}", LogLevel.Error); }
                    Dispose();
                }
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
            lock (_gate) _callbackDepth--;
        }
    }

    private void UnregisterCore()
    {
        IntPtr notification;
        lock (_gate)
        {
            notification = _notification;
            _notification = IntPtr.Zero;
        }
        // CM waits for callbacks: neither the admission gate nor an owner's gate may be held here.
        if (notification != IntPtr.Zero)
            DeviceNotificationNative.CM_Unregister_Notification(notification);
        CloseTargetHandle();
        GC.KeepAlive(_callback);
    }

    private void CloseTargetHandle() => Interlocked.Exchange(ref _targetHandle, null)?.Dispose();

    private void RequestRemoval()
    {
        var requested = false;
        if (!TryInvoke(() =>
        {
            CloseTargetHandle();
            requested = !_removalRequested;
            _removalRequested = true;
        }) || !requested) return;

        // Owner callbacks fence their short side effects with TryInvoke (and their registration identity),
        // but must join their monitor outside this gate so a loop replacing its handle can finish.
        try { _onRemovalRequested(this); }
        catch (Exception ex) { Logger.Log($"[DeviceNotification] Failed to release a drive monitor: {ex.Message}", LogLevel.Error); }
    }

    private void RestoreAfterFailedRemoval()
    {
        if (!TryInvoke(() => _removalRequested = false)) return;
        try { _onRemovalFailed(this); }
        catch (Exception ex) { Logger.Log($"[DeviceNotification] Failed to restore a drive monitor: {ex.Message}", LogLevel.Error); }
        // CM keeps the registration valid after QUERYREMOVEFAILED even though its handle was closed.
        // The owner recreates its monitor/handle; opening a second handle here could race that recovery.
    }
}
