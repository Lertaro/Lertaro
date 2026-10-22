using System.Windows.Media;
using System.Windows.Media.Imaging;
using Lertaro.App.Services.ShellIcons;

namespace Lertaro.App.Tests.Services.ShellIcons;

// One guard now bounds both questions the host asks a thumbnail provider: the thumbnail itself, which used to
// hold an icon-loader permit open forever, and the can-you-handle-this-path question, asked on the UI thread
// for every visible row. Measured 2026-10-22 from a user's full dump: 4 hung GetImage calls, 163 rows queued
// behind them, 38 idle workers.
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

        var result = ThumbnailCallGuard.Run<ImageSource?>(() => image, @"C:\any.png");

        Assert.AreSame(image, result);
        Assert.IsFalse(ThumbnailCallGuard.InCooldown);
    }

    [StaTestMethod]
    public void Run_ProviderWedgedPastBudget_ReturnsNullAndTripsCooldown()
    {
        ThumbnailCallGuard.TimeoutMs = 50;
        var gate = new ManualResetEventSlim(false);
        try
        {
            var result = ThumbnailCallGuard.Run<ImageSource?>(() =>
            {
                gate.Wait();
                return null;
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
    public void Run_WedgedCanProvideQuestion_AnswersNoAndTripsCooldown()
    {
        ThumbnailCallGuard.TimeoutMs = 50;
        var gate = new ManualResetEventSlim(false);
        try
        {
            var answer = ThumbnailCallGuard.Run(() =>
            {
                gate.Wait();
                return true;
            }, @"C:\wedged.mp4");

            Assert.IsFalse(answer, "a provider that cannot answer the yes/no question counts as no help at all");
            Assert.IsTrue(ThumbnailCallGuard.InCooldown);
        }
        finally
        {
            gate.Set();
        }
    }

    [StaTestMethod]
    public void Run_WhileInCooldown_SkipsBothProviderQuestions()
    {
        ThumbnailCallGuard.CooldownUntilUtcTicks = DateTime.UtcNow.AddMinutes(1).Ticks;
        var thumbnailAsked = false;
        var canProvideAsked = false;

        var thumbnail = ThumbnailCallGuard.Run<ImageSource?>(() =>
        {
            thumbnailAsked = true;
            return new BitmapImage();
        }, @"C:\any.png");
        var canProvide = ThumbnailCallGuard.Run(() =>
        {
            canProvideAsked = true;
            return true;
        }, @"C:\any.png");

        Assert.IsNull(thumbnail);
        Assert.IsFalse(canProvide);
        Assert.IsFalse(thumbnailAsked, "one wedged handler is probed once per cooldown window, not once per row");
        Assert.IsFalse(canProvideAsked, "the UI-thread question is skipped for the same window");
    }

    [StaTestMethod]
    public void Run_ProviderFastsWithAnException_LeavesCooldownAlone()
    {
        Assert.ThrowsExactly<AggregateException>(() =>
            ThumbnailCallGuard.Run<ImageSource?>(() => throw new InvalidOperationException("bad provider"), @"C:\any.png"));

        Assert.IsFalse(ThumbnailCallGuard.InCooldown, "a provider that answers fast, even badly, must not freeze every provider's calls");
    }
}
