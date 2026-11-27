using System.Runtime.InteropServices;
using Lertaro.Core.DriveMonitoring;
using Lertaro.Core.Indexer.Usn;
using Lertaro.Core.Tests.IndexV2;

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
    [DataRow(DeviceNotificationNative.DeviceCustomEvent, "9a8c3d68-d0cb-11d1-8fef-00a0c9a06d32")]
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

    // The non-blocking release a walk recorder uses: it leaves the unregistration to the pool, but the
    // registration must stop admitting callbacks at once, like Dispose.
    [TestMethod]
    public void DisposeWithoutWaiting_RejectsEveryLaterCallback()
    {
        var calls = 0;
        var monitor = new DriveDeviceRemovalMonitor("C", _ => calls++, _ => calls++, _ => calls++);

        monitor.DisposeWithoutWaiting();
        Notify(monitor, DeviceNotificationNative.DeviceQueryRemove);
        Notify(monitor, DeviceNotificationNative.DeviceRemoveComplete);
        monitor.Dispose();

        Assert.AreEqual(0, calls);
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

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task HandleNotification_VolumeLock_WaitsForAccessAndRestoresOnce(bool deliverUnlock)
    {
        var root = Directory.CreateTempSubdirectory("lertaro-volume-recovery-").FullName;
        var unavailable = Path.Combine(root, "not-ready");
        var requests = 0;
        var restored = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var restores = 0;
        using var monitor = new DriveDeviceRemovalMonitor(unavailable, _ => requests++, _ =>
        {
            Interlocked.Increment(ref restores);
            restored.TrySetResult();
        }, _ => Assert.Fail("An unlock is not a completed removal."));
        try
        {
            NotifyCustom(monitor, DeviceNotificationNative.VolumeLockEvent);
            if (deliverUnlock) NotifyCustom(monitor, DeviceNotificationNative.VolumeUnlockEvent);
            await Task.Delay(TimeSpan.FromSeconds(2.2));
            Assert.IsFalse(restored.Task.IsCompleted, "A still-inaccessible volume must not start a rebuild.");
            Directory.CreateDirectory(unavailable);
            await restored.Task.WaitAsync(Deadline);
            NotifyCustom(monitor, DeviceNotificationNative.VolumeUnlockEvent);
            Notify(monitor, DeviceNotificationNative.DeviceQueryRemoveFailed);
            Assert.AreEqual(1, restores);
            Notify(monitor, DeviceNotificationNative.DeviceQueryRemove);
            Assert.AreEqual(2, requests, "Recovery must clear the previous removal request.");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [TestMethod]
    public async Task HandleNotification_DisposedWhileWaitingForUnlock_DoesNotRestore()
    {
        var restores = 0;
        var monitor = new DriveDeviceRemovalMonitor(Path.GetTempPath(), _ => { }, _ => restores++, _ => { });
        NotifyCustom(monitor, DeviceNotificationNative.VolumeLockEvent);
        monitor.Dispose();
        await Task.Delay(TimeSpan.FromSeconds(2.2));
        Assert.AreEqual(0, restores);
    }

    [TestMethod]
    public async Task HandleNotification_LockWithAnOwnedIndex_UnlockQueuesForcedRecovery()
    {
        var root = Directory.CreateTempSubdirectory("lertaro-owned-lock-").FullName;
        using var fixture = LiveIndexFixture.Build(root, new[] { LiveIndexFixture.Root() });
        using var indexer = new UsnIndexer();
        indexer._recordIndexes[root] = fixture.Index;
        var stopped = new StopCounter();
        var registration = new DriveMonitorRegistration(stopped, null);
        indexer.RegisterDriveMonitor(root, registration);
        var cancelled = 0;
        var recovered = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var removal = DriveMonitorFactory.CreateRemovalMonitor(indexer, root, registration, CancellationToken.None,
            _ => Assert.Fail("Lock recovery must preserve the forced rebuild request."), _ => cancelled++,
            drive => recovered.TrySetResult(drive));
        Assert.IsTrue(registration.ReplaceRemovalMonitor(removal));
        try
        {
            NotifyCustom(removal, DeviceNotificationNative.VolumeLockEvent);
            Assert.AreEqual(1, stopped.Calls);
            Assert.AreEqual(1, cancelled);
            Assert.IsFalse(indexer._recordIndexes.ContainsKey(root));
            Assert.ThrowsExactly<ObjectDisposedException>(() => fixture.Index.GetCounts());
            NotifyCustom(removal, DeviceNotificationNative.VolumeUnlockEvent);
            Assert.AreEqual(root, await recovered.Task.WaitAsync(Deadline));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private sealed class StopCounter : IDisposable
    {
        public int Calls { get; private set; }
        public void Dispose() => Calls++;
    }

    private static void NotifyCustom(DriveDeviceRemovalMonitor monitor, Guid eventGuid)
    {
        var data = Marshal.AllocHGlobal(24);
        try
        {
            Marshal.StructureToPtr(eventGuid, IntPtr.Add(data, 8), false);
            Assert.AreEqual(DeviceNotificationNative.Success,
                monitor.HandleNotification(IntPtr.Zero, IntPtr.Zero, DeviceNotificationNative.DeviceCustomEvent, data, 24));
        }
        finally { Marshal.FreeHGlobal(data); }
    }
}
