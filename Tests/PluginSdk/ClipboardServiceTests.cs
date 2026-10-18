using Lertaro.PluginSdk.Services;

namespace Lertaro.PluginSdk.Tests;

// ClipboardService.GetTextFunc is a shared static delegate, and this assembly parallelises at method
// level, so [DoNotParallelize] plus the reset below keeps these tests from seeing each other's wiring.
[TestClass]
[DoNotParallelize]
public sealed class ClipboardServiceTests
{
    [TestInitialize]
    public void Reset() => ClipboardService.GetTextFunc = null;

    [TestMethod]
    public void GetText_NoHostWired_ReturnsNull() => Assert.IsNull(ClipboardService.GetText());

    [TestMethod]
    public void GetText_HostReturnsText_ReturnsIt()
    {
        ClipboardService.GetTextFunc = () => "copied text";

        Assert.AreEqual("copied text", ClipboardService.GetText());
    }

    [TestMethod]
    public void GetText_ClipboardHoldsNoText_ReturnsNull()
    {
        ClipboardService.GetTextFunc = () => null;

        Assert.IsNull(ClipboardService.GetText());
    }

    [TestMethod]
    public void GetText_HostThrows_ReturnsNullInsteadOfThrowing()
    {
        ClipboardService.GetTextFunc = () => throw new InvalidOperationException("clipboard owned elsewhere");

        Assert.IsNull(ClipboardService.GetText());
    }
}
