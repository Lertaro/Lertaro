using System.Windows.Media;
using System.Windows.Media.Imaging;
using Lertaro.App.Services.ShellIcons;

namespace Lertaro.App.Tests.Services.ShellIcons;

// The guard is what keeps one wedged plugin thumbnail call from holding an icon-loader permit open forever,
// which is how every result row in a user's quick window stayed on its placeholder (measured 2026-10-22 from
// his full dump: 4 hung GetImage calls, 163 rows queued behind them, 38 idle workers).
[TestClass]
[DoNotParallelize]
public sealed class ThumbnailCallGuardTests
{
    private int _savedTimeoutMs = ThumbnailCallGuard.TimeoutMs;
    private int _savedCooldownMs = ThumbnailCallGuard.CooldownMs;

    [TestInitialize]
    public void Setup()
    {
        _savedTimeoutMs = ThumbnailCallGuard.TimeoutMs;
        _savedCooldownMs = ThumbnailCallGuard.CooldownMs;
        ThumbnailCallGuard.CooldownUntilUtcTicks = 0;
    }

    [TestCleanup]
    public void Cleanup()
    {
        ThumbnailCallGuard.TimeoutMs = _savedTimeoutMs;
        ThumbnailCallGuard.CooldownMs = _savedCooldownMs;
        ThumbnailCallGuard.CooldownUntilUtcTicks = 0;
    }

    [StaTestMethod]
    public void Run_ProviderAnswersWithinBudget_ReturnsItsImage()
    {
        var image = new BitmapImage();

        Assert.AreSame(image, ThumbnailCallGuard.Run(() => image, @"C:\any.png"));
        Assert.IsFalse(ThumbnailCallGuard.InCooldown);
    }

    [StaTestMethod]
    public void Run_ProviderWedgedPastBudget_ReturnsNullAndTripsCooldown()
    {
        ThumbnailCallGuard.TimeoutMs = 50;
        var gate = new ManualResetEventSlim(false);
        try
        {
            var result = ThumbnailCallGuard.Run(() =>
            {
                gate.Wait();
                return (ImageSource?)null;
            }, @"C:\wedged.mp4");

            Assert.IsNull(result, "a wedged provider must not keep the caller waiting past the budget");
            Assert.IsTrue(ThumbnailCallGuard.InCooldown);
        }
        finally
        {
            // Let the parked worker finish; the guard deliberately cannot interrupt it.
            gate.Set();
        }
    }

    [StaTestMethod]
    public void Run_WhileInCooldown_SkipsTheProviderAltogether()
    {
        ThumbnailCallGuard.CooldownUntilUtcTicks = DateTime.UtcNow.AddMinutes(1).Ticks;
        var called = false;

        var result = ThumbnailCallGuard.Run(() =>
        {
            called = true;
            return new BitmapImage();
        }, @"C:\any.png");

        Assert.IsNull(result);
        Assert.IsFalse(called, "one wedged handler is probed once per cooldown window, not once per row");
    }

    [StaTestMethod]
    public void Run_ProviderFastsWithAnException_LeavesCooldownAlone()
    {
        Assert.ThrowsExactly<AggregateException>(() =>
            ThumbnailCallGuard.Run(() => throw new InvalidOperationException("bad provider"), @"C:\any.png"));

        Assert.IsFalse(ThumbnailCallGuard.InCooldown, "a provider that answers fast, even badly, must not freeze every provider's results");
    }
}
