using System.Windows;
using Lertaro.Plugins.CoreExtensions.Preview.Handlers;

namespace Lertaro.Plugins.CoreExtensions.Tests.Preview.Handlers;

// TopmostBand is the #459 relief valve: our own windows hand back the topmost band for the length of one native
// preview render, because Windows does not keep a non-topmost owned window above a topmost owner -- so the handler's
// prompt sits invisible underneath the window whose keyboard it holds. The rule worth pinning is that only windows
// that were ALREADY topmost are lowered, and exactly those get it back: restoring must not raise a window the user
// never had on top. The owner-chain walk itself needs shown windows (WPF refuses an Owner that was never displayed),
// so it is covered by the live preview repro rather than here.
[STATestClass]
public sealed class TopmostBandTests
{
    [STATestMethod]
    public void Suspend_LowersAWindowThatWasTopmostAndHandsItBack()
    {
        var window = new Window { Topmost = true };

        var lowered = TopmostBand.Suspend(window);

        CollectionAssert.AreEqual(new[] { window }, lowered);
        Assert.IsFalse(window.Topmost);
    }

    [STATestMethod]
    public void Suspend_LeavesAWindowThatWasNeverTopmostAlone()
    {
        var window = new Window();

        var lowered = TopmostBand.Suspend(window);

        Assert.AreEqual(0, lowered.Count);
        Assert.IsFalse(window.Topmost);
    }

    [STATestMethod]
    public void Restore_GivesTheBandBackToExactlyWhatWasLowered()
    {
        var loweredWas = new Window { Topmost = true };
        var neverTopmost = new Window();

        TopmostBand.Restore(TopmostBand.Suspend(loweredWas));

        Assert.IsTrue(loweredWas.Topmost);
        Assert.IsFalse(neverTopmost.Topmost);
    }

    [STATestMethod]
    public void Suspend_WithoutAWindowLowersNothingAndRestoreAcceptsThat()
    {
        var lowered = TopmostBand.Suspend(null);

        Assert.AreEqual(0, lowered.Count);
        TopmostBand.Restore(lowered);
    }
}
