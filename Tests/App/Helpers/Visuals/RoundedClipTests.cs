using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Lertaro.App.Helpers.Visuals;

namespace Lertaro.App.Tests.Helpers.Visuals;

// RoundedClip must release the Border it watched when the window closes.
//
// The bug this guards against is invisible in any value the code returns: RoundedClip tracked
// CornerRadius with DependencyPropertyDescriptor.AddValueChanged, which registers the Border in a
// PROCESS-WIDE static table. RemoveValueChanged is the only thing that takes it back out, and it was
// never called -- so every closed window stayed reachable (measured: 17 live InlineSearchWindow
// instances, a weak-event table grown to 21MB, and every later UI-thread event dispatch slower than the
// last). That is the "settings > Plugins gets laggy after a while, and a restart fixes it" report.
//
// [DoNotParallelize] because this shows and closes real windows on its own dispatcher thread, which is
// process-wide.
[TestClass]
[DoNotParallelize]
public sealed class RoundedClipTests
{
    [StaTestMethod]
    public void AClosedWindowsBorder_BecomesCollectable()
    {
        var (borderRef, windowRef) = ShowAndCloseWindowWithRoundedClip();

        Assert.IsFalse(IsAliveAfterFullCollection(borderRef),
            "the Border must not stay pinned by RoundedClip's value-changed registration after the window closes");
        Assert.IsFalse(IsAliveAfterFullCollection(windowRef),
            "and the window it belonged to must be collectable with it");
    }

    [StaTestMethod]
    public void DetachingIsWhatMakesItCollectable()
    {
        // Same shape, but keeping the AddValueChanged registration alive for the Border's lifetime (no
        // detach). If this ever stops pinning, the test above would pass for the wrong reason and stop
        // guarding anything.
        var (borderRef, windowRef) = ShowAndCloseWindowWithDetach(detach: false);

        Assert.IsTrue(IsAliveAfterFullCollection(borderRef) || IsAliveAfterFullCollection(windowRef),
            "without the detach, the value-changed registration should still pin the window -- "
            + "this proves the test above is measuring the fix and not just GC timing");
    }

    private static (WeakReference Border, WeakReference Window) ShowAndCloseWindowWithRoundedClip() =>
        ShowAndCloseWindowWithDetach(detach: true);

    private static (WeakReference Border, WeakReference Window) ShowAndCloseWindowWithDetach(bool detach)
    {
        WeakReference? borderRef = null;
        WeakReference? windowRef = null;
        Exception? failure = null;

        // Its own thread and Dispatcher: Window.Show()/Close() need one, and MSTest's default thread
        // would leave this test's window registered against a dispatcher other tests also use.
        var thread = new Thread(() =>
        {
            try
            {
                var window = new Window { Width = 200, Height = 120 };
                var border = new Border { CornerRadius = new CornerRadius(6) };
                window.Content = border;
                RoundedClip.SetIsEnabled(border, true);

                if (!detach)
                {
                    // Reproduce the old behaviour: a registration that outlives the Border.
                    var descriptor = DependencyPropertyDescriptor.FromProperty(Border.CornerRadiusProperty, typeof(Border));
                    descriptor.AddValueChanged(border, (_, _) => { });
                }

                window.Show();
                window.Close();

                // Drain anything the dispatcher queued for the close (Unloaded lands here).
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

                borderRef = new WeakReference(border);
                windowRef = new WeakReference(window);
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(30)), "the window thread should finish");

        // The thread's own references are gone once it exits; clear ours too so only the WPF static
        // table can still be holding the objects.
        GC.KeepAlive(borderRef);
        if (failure != null) throw failure;

        return (borderRef!, windowRef!);
    }

    private static bool IsAliveAfterFullCollection(WeakReference reference)
    {
        for (var i = 0; i < 4; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
        return reference.IsAlive;
    }
}
