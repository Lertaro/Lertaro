using System.Runtime.InteropServices;
using Lertaro.Core.DriveMonitoring;

namespace Lertaro.Core.Tests.DriveMonitoring;

[TestClass]
public sealed class DriveDeviceRemovalMonitorTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

    [TestMethod]
    [DataRow(DeviceNotificationNative.DeviceQueryRemove, null)]
    [DataRow(DeviceNotificationNative.DeviceRemovePending, null)]
    [DataRow(DeviceNotificationNative.DeviceQueryRemoveFailed, null)]
    [DataRow(DeviceNotificationNative.DeviceRemoveComplete, null)]
    [DataRow(DeviceNotificationNative.DeviceCustomEvent, "50708874-c9af-11d1-8fef-00a0c9a06d32")]
    [DataRow(DeviceNotificationNative.DeviceCustomEvent, "ae2eed10-0ba8-11d2-8ffb-00a0c9a06d32")]
    [DataRow(DeviceNotificationNative.DeviceCustomEvent, "d16a55e8-1059-11d2-8ffd-00a0c9a06d32")]
    [DataRow(DeviceNotificationNative.DeviceCustomEvent, "e3c5b178-105d-11d2-8ffd-00a0c9a06d32")]
    public void HandleNotification_AfterDispose_RejectsEveryRemovalAction(int action, string? eventId)
    {
        var calls = 0;
        var monitor = new DriveDeviceRemovalMonitor("C", _ => calls++, _ => calls++, _ => calls++);
        monitor.Dispose();
        var data = eventId == null ? IntPtr.Zero : Marshal.AllocHGlobal(24);
        try
        {
            if (eventId != null) Marshal.StructureToPtr(Guid.Parse(eventId), IntPtr.Add(data, 8), false);
            Assert.AreEqual(DeviceNotificationNative.Success,
                monitor.HandleNotification(IntPtr.Zero, IntPtr.Zero, action, data, eventId == null ? 0u : 24u));
            Assert.AreEqual(0, calls, "An invalidated native registration admitted a callback.");
        }
        finally
        {
            if (data != IntPtr.Zero) Marshal.FreeHGlobal(data);
        }
    }

    [TestMethod]
    public void HandleNotification_QueryFailedThenQueryAgain_ReleasesOncePerAttempt()
    {
        var requests = 0;
        var restores = 0;
        using var monitor = new DriveDeviceRemovalMonitor("C", _ => requests++, _ => restores++, _ => { });

        Notify(monitor, DeviceNotificationNative.DeviceQueryRemove);
        Notify(monitor, DeviceNotificationNative.DeviceRemovePending);
        Assert.AreEqual(1, requests);
        Notify(monitor, DeviceNotificationNative.DeviceQueryRemoveFailed);
        Notify(monitor, DeviceNotificationNative.DeviceQueryRemove);
        Assert.AreEqual(2, requests);
        Assert.AreEqual(1, restores);
    }

    [TestMethod]
    public async Task Dispose_WithAnAdmittedCallback_InvalidatesItsEffectsWithoutWaitingForIt()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var requests = 0;
        var restores = 0;
        var effectAccepted = true;
        using var monitor = new DriveDeviceRemovalMonitor("C", source =>
        {
            entered.Set();
            release.Wait();
            effectAccepted = source.TryInvoke(() => requests++);
        }, _ => restores++, _ => restores++);
        var callback = Task.Run(() => Notify(monitor, DeviceNotificationNative.DeviceQueryRemove));
        try
        {
            Assert.IsTrue(entered.Wait(Deadline));
            await Task.Run(monitor.Dispose).WaitAsync(Deadline);
            // CM may still deliver these while unregistration is deferred behind the first callback.
            Notify(monitor, DeviceNotificationNative.DeviceQueryRemoveFailed);
            Notify(monitor, DeviceNotificationNative.DeviceRemoveComplete);
        }
        finally
        {
            release.Set();
            await callback.WaitAsync(Deadline);
        }
        Assert.IsFalse(effectAccepted);
        Assert.AreEqual(0, requests);
        Assert.AreEqual(0, restores);
    }

    private static void Notify(DriveDeviceRemovalMonitor monitor, int action) =>
        Assert.AreEqual(DeviceNotificationNative.Success,
            monitor.HandleNotification(IntPtr.Zero, IntPtr.Zero, action, IntPtr.Zero, 0));
}
