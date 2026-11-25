using Lertaro.Core.Hook.InlineSearch;

namespace Lertaro.Core.Tests.Hook.InlineSearch;

[TestClass]
public sealed class MouseHookServiceTests
{
    [TestMethod]
    [DataRow(0x0200, 0)]
    [DataRow(0x0201, 1)]
    [DataRow(0x0202, 1)]
    [DataRow(0x0204, 1)]
    [DataRow(0x0207, 1)]
    [DataRow(0x020A, 1)]
    [DataRow(0x020B, 1)]
    [DataRow(0x020C, 1)]
    [DataRow(0x020E, 1)]
    public void NotifyButtonOrWheel_OnlyActionsCancelKeyboardGesture(int message, int expected)
    {
        using var service = new MouseHookService();
        var cancellations = 0;
        service.OnMouseButtonOrWheel += () => cancellations++;
        service.NotifyButtonOrWheel(message);
        Assert.AreEqual(expected, cancellations);
    }
}
