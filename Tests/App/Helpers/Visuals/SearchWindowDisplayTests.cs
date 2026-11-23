using Lertaro.App.Helpers.Visuals;

namespace Lertaro.App.Tests.Helpers.Visuals;

[TestClass]
public sealed class SearchWindowDisplayTests
{
    [TestMethod]
    [DataRow(null, -1)]
    [DataRow("", -1)]
    [DataRow(@"\\.\DISPLAY2", 1)]
    [DataRow(@"\\.\display2", 1)]
    [DataRow(@"\\.\DISPLAY3", -1)]
    public void ResolveDisplayIndex_UsesTheConfiguredDeviceOrCursorFallback(string? device, int expectedIndex) =>
        Assert.AreEqual(expectedIndex, SearchWindowDisplay.ResolveDisplayIndex(device, [@"\\.\DISPLAY1", @"\\.\DISPLAY2"]));

    [TestMethod]
    public void ResolveDisplayIndex_ReorderedDisplays_StillFindsTheSavedDevice() =>
        Assert.AreEqual(0, SearchWindowDisplay.ResolveDisplayIndex(@"\\.\DISPLAY2", [@"\\.\DISPLAY2", @"\\.\DISPLAY1"]));

    [TestMethod]
    public void ResolveDisplayIndex_DisconnectedDisplay_FallsBackWithoutChangingTheSavedName()
    {
        const string savedDevice = @"\\.\DISPLAY2";
        Assert.AreEqual(-1, SearchWindowDisplay.ResolveDisplayIndex(savedDevice, []));
        Assert.AreEqual(0, SearchWindowDisplay.ResolveDisplayIndex(savedDevice, [savedDevice]));
    }
}
