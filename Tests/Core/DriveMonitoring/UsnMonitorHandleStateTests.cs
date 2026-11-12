using Microsoft.Win32.SafeHandles;
using Lertaro.Core.DriveMonitoring;

namespace Lertaro.Core.Tests.DriveMonitoring;

[TestClass]
public sealed class UsnMonitorHandleStateTests
{
    [TestMethod]
    public void TrySet_AfterDispose_RejectsAndDisposesTheLateHandle()
    {
        var state = new UsnMonitorHandleState();
        state.Dispose();
        using var late = new SafeFileHandle(new IntPtr(123), ownsHandle: false);

        Assert.IsFalse(state.TrySet(late));
        Assert.IsTrue(late.IsClosed);
    }

    [TestMethod]
    public void Clear_DoesNotDisposeTheCurrentHandleBeforeTheMonitorOwnsItsFinally()
    {
        var state = new UsnMonitorHandleState();
        using var handle = new SafeFileHandle(new IntPtr(123), ownsHandle: false);

        Assert.IsTrue(state.TrySet(handle));
        state.Clear(handle);

        Assert.IsFalse(handle.IsClosed);
        state.Dispose();
    }
}
