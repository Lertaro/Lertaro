using System.Runtime.InteropServices;
using Lertaro.App.Services.Notifications;

namespace Lertaro.App.Tests.Services.Notifications;

[TestClass]
public sealed class NotificationSessionStateTests
{
    [TestMethod]
    [DataRow(1, 0, 20, true)]
    [DataRow(1, 1, 20, false)]
    [DataRow(1, -1, 20, null)]
    [DataRow(2, 0, 20, null)]
    [DataRow(1, 0, 19, null)]
    public void Parse_ValidatesTheNativePrefixAndDecodesSessionFlags(int level, int flags, int bytes, bool? expected)
    {
        var buffer = Marshal.AllocHGlobal(20);
        try
        {
            Marshal.WriteInt32(buffer, level);
            Marshal.WriteInt32(buffer, 16, flags);

            Assert.AreEqual(expected, NotificationSessionState.Parse(buffer, (uint)bytes));
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    [TestMethod]
    public void Parse_AbsentBuffer_IsUnknownRatherThanUnlocked() =>
        Assert.IsNull(NotificationSessionState.Parse(IntPtr.Zero, 20));
}
