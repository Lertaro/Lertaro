using Lertaro.App.Helpers.App;

namespace Lertaro.App.Tests.Helpers.App;

[TestClass]
public sealed class AppNativeHelperTests
{
    [TestMethod]
    public void GetProcessNameOfWindow_ZeroHwnd_ReturnsUnknown()
    {
        var result = AppNativeHelper.GetProcessNameOfWindow(IntPtr.Zero);
        Assert.AreEqual("Unknown", result);
    }

    [TestMethod]
    public void GetClassNameOfWindow_ZeroHwnd_ReturnsUnknown()
    {
        var result = AppNativeHelper.GetClassNameOfWindow(IntPtr.Zero);
        Assert.AreEqual("Unknown", result);
    }
}
