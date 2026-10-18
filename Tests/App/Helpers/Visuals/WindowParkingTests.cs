using System.Windows;
using System.Windows.Threading;
using Lertaro.App.Helpers.Visuals;

namespace Lertaro.App.Tests.Helpers.Visuals;

// The whole black-frame fix rests on two facts about the parking row, and neither is visible in a return value:
// a card has to be shown there rather than where the user is looking, and it has to keep its target x while it
// is there -- that x is what makes the parked frame be composed for the monitor the card is going to land on, so
// landing it changes neither its size nor its DPI context. Both are asserted here; what is left for a running
// desktop is whether the compositor puts a frame up in time, which no in-process test can see.
//
// [DoNotParallelize] because these show real windows on the process's dispatcher.
[TestClass]
[DoNotParallelize]
public sealed class WindowParkingTests
{
    [StaTestMethod]
    public void Park_TakesBothCoordinates()
    {
        var window = new Window { Left = 800, Top = 600 };

        WindowParking.Park(window);

        Assert.AreEqual(WindowParking.OffScreen, window.Left, "Left alone keeps Windows using CW_USEDEFAULT for x and ignoring y");
        Assert.AreEqual(WindowParking.OffScreen, window.Top);
    }

    [StaTestMethod]
    public void ShowThenPlace_ShowsAtTheParkingRowAndKeepsTheTargetX()
    {
        var window = new Window { Left = 1920, Top = 400 };
        var landed = false;

        WindowParking.ShowThenPlace(window, () => landed = true);

        try
        {
            Assert.AreEqual(WindowParking.OffScreen, window.Top, "the card is shown one screen-height above where it belongs");
            Assert.AreEqual(1920, window.Left, "and its x is left where the caller placed it: that column is its target monitor");
            Assert.IsTrue(window.IsVisible, "shown, not merely positioned -- WPF has to have something to render to");
            Assert.IsFalse(landed, "the landing pass is queued, not run inside the call");
        }
        finally
        {
            window.Close();
        }
    }

    [StaTestMethod]
    public void AfterFrame_RunsOnTheFirstPassBehindTheRenderQueue()
    {
        var window = new Window();
        var order = new List<string>();

        WindowParking.AfterFrame(window, () => order.Add("landed"));
        Assert.AreEqual(0, order.Count, "nothing may run inside the call that asked for the pass");

        // Pump to SystemIdle, the first priority behind Loaded: if the landing pass has not run by then, it is
        // queued somewhere the card would never reach.
        var frame = new DispatcherFrame();
        window.Dispatcher.BeginInvoke(DispatcherPriority.SystemIdle,
            new Action(() => { order.Add("idle"); frame.Continue = false; }));
        Dispatcher.PushFrame(frame);

        CollectionAssert.AreEqual(new[] { "landed", "idle" }, order);
    }

    [StaTestMethod]
    public void ParkUntilPainted_LiftsAWindowWpfPlacedAndBringsItBack()
    {
        var window = new Window { Left = 700, Top = 300 };

        WindowParking.ParkUntilPainted(window);
        window.Show();

        try
        {
            Assert.AreEqual(WindowParking.OffScreen, window.Top,
                "the window is in the parking row at the instant it becomes visible");
            Assert.AreEqual(700, window.Left, "and only vertically: the x is what keeps its monitor's scale");
        }
        finally
        {
            // Pump past Loaded so the restore pass runs, or the test leaves a window parked off screen.
            var frame = new DispatcherFrame();
            window.Dispatcher.BeginInvoke(DispatcherPriority.SystemIdle,
                new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
            Assert.AreEqual(300, window.Top, "and it is back where WPF put it once the render queue has been through");
            window.Close();
        }
    }

    [StaTestMethod]
    public void ParkUntilPainted_SaysNothingToAWindowThatAlreadyHasAHandle()
    {
        var window = new Window { Left = 700, Top = 300 };
        window.Show();
        window.Hide();

        // A re-shown cached window has already painted once and will never raise SourceInitialized again, so a
        // handler registered here would just stack up on every reopen and park nothing.
        WindowParking.ParkUntilPainted(window);
        window.Show();

        Assert.AreEqual(300, window.Top, "the second show goes up where it was put, unparked");
        window.Close();
    }
}
