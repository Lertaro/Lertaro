using Lertaro.Core.Hook.InlineSearch;

namespace Lertaro.Core.Tests.Hook;

[TestClass]
public sealed class QuickNavDoubleClickTests
{
    private static bool Press(QuickNavDoubleClick clicks, uint time, int x = 50, int view = 12) =>
        clicks.Press(view, x, 10, time, 500, 4, 4);

    [TestMethod]
    public void FourClicksAtOnePositionFormTwoDisjointPairs()
    {
        var clicks = new QuickNavDoubleClick((_, _, _) => null);
        CollectionAssert.AreEqual(new[] { false, true, false, true },
            new uint[] { 100, 200, 300, 400 }.Select(time => Press(clicks, time)).ToArray());
    }

    [TestMethod]
    public void FolderClickFollowedByEmptyViewCannotTriggerAcrossNavigation()
    {
        var samples = new Queue<uint?>([null, 340]);
        var clicks = new QuickNavDoubleClick((_, _, _) => Task.FromResult(samples.Dequeue()));
        CollectionAssert.AreEqual(new[] { false, false, false, true },
            new uint[] { 100, 200, 300, 400 }.Select(time => Press(clicks, time)).ToArray());
    }

    [TestMethod]
    [DataRow(500, true)]
    [DataRow(501, false)]
    [DataRow(502, false)]
    public void BackgroundMustBeConfirmedBeforeSecondHardwareTimestamp(int sampled, bool expected)
    {
        var clicks = new QuickNavDoubleClick((_, _, _) => Task.FromResult<uint?>((uint)sampled));
        Assert.IsFalse(Press(clicks, 100));
        Assert.AreEqual(expected, Press(clicks, 501));
    }

    [TestMethod]
    public void BusyProviderCannotDelayOrRetroactivelyAuthorizeADoubleClick()
    {
        var pending = new TaskCompletionSource<uint?>();
        var clicks = new QuickNavDoubleClick((_, _, _) => pending.Task);
        Assert.IsFalse(Press(clicks, 100));
        Assert.IsFalse(Press(clicks, 200));
        pending.SetResult(250);
        Assert.IsFalse(Press(clicks, 300));
    }

    [TestMethod]
    public void ChangingViewsMovementTimeoutAndOtherButtonsRestartThePair()
    {
        var clicks = new QuickNavDoubleClick((_, _, _) => null);
        Assert.IsFalse(Press(clicks, 100));
        Assert.IsFalse(Press(clicks, 200, view: 13));
        Assert.IsFalse(Press(clicks, 300, x: 55, view: 13));
        Assert.IsFalse(Press(clicks, 900, x: 55, view: 13));
        clicks.Clear();
        Assert.IsFalse(Press(clicks, 950, x: 55, view: 13));
        Assert.IsTrue(Press(clicks, 1000, x: 55, view: 13));
    }

    [TestMethod]
    public void DoubleClickAcrossTickWrapAcceptsTimelyBackground()
    {
        var clicks = new QuickNavDoubleClick((_, _, _) => Task.FromResult<uint?>(5));
        Assert.IsFalse(Press(clicks, uint.MaxValue - 50));
        Assert.IsTrue(Press(clicks, 25));
    }
}
