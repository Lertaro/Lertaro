using Lertaro.Core.DriveMonitoring;

namespace Lertaro.Core.Tests.DriveMonitoring;

[TestClass]
[DoNotParallelize]
public sealed class DeviceNotificationNativeTests
{
    [TestMethod]
    [DataRow(0, 1)] // CM_NOTIFY_ACTION_DEVICEINTERFACEARRIVAL
    [DataRow(1, 0)] // CM_NOTIFY_ACTION_DEVICEINTERFACEREMOVAL
    [DataRow(2, 0)] // CM_NOTIFY_ACTION_DEVICEQUERYREMOVE
    [DataRow(5, 0)] // CM_NOTIFY_ACTION_DEVICEREMOVECOMPLETE
    [DataRow(0x8000, 0)] // DBT_DEVICEARRIVAL belongs to a different API
    [DataRow(0x8004, 0)] // DBT_DEVICEREMOVECOMPLETE
    public void HandleNotification_OnlyNativeInterfaceArrivalWakesSubscribers(int nativeAction, int expectedCalls)
    {
        var calls = 0;
        void OnArrived(object? sender, EventArgs args) => calls++;
        VolumeArrivalMonitor.Arrived += OnArrived;
        try
        {
            var result = VolumeArrivalMonitor.HandleNotification(IntPtr.Zero, IntPtr.Zero, nativeAction, IntPtr.Zero, 0);

            Assert.AreEqual(0u, result);
            Assert.AreEqual(expectedCalls, calls);
        }
        finally
        {
            VolumeArrivalMonitor.Arrived -= OnArrived;
        }
    }
}
